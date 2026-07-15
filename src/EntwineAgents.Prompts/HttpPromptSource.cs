using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using EntwineAgents.Runtime;

namespace EntwineAgents.Prompts;

/// <summary>
/// An <see cref="IPromptSource"/> backed by a remote prompt-egress endpoint
/// (<c>GET {base}/api/prompt-egress/{key}</c>, X-Api-Key auth) — how a satellite host (e.g. the concierge
/// funnel, which deliberately has no database) binds REAL versioned, per-client prompts without referencing
/// the app or its DbContext. Fail-open by design: a 404 (no stored override), a network error, or an
/// unreadable body all fall back to the caller's built-in prompt — a prompt-service outage can never take an
/// agent down. Resolutions are cached briefly so hot paths don't pay a network hop per call.
/// </summary>
public sealed class HttpPromptSource(HttpClient http, IMemoryCache? cache = null, ILogger<HttpPromptSource>? log = null) : IPromptSource
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string> GetAsync(string key, string fallback, CancellationToken ct = default)
    {
        var cacheKey = $"prompt-egress:{key}";
        if (cache is not null && cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached.Length > 0 ? cached : fallback;   // empty sentinel = "no override" cached

        string resolved;
        try
        {
            using var response = await http.GetAsync($"api/prompt-egress/{Uri.EscapeDataString(key)}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                resolved = string.Empty;   // no stored override — remember that, use the fallback
            }
            else
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<EgressPrompt>(cancellationToken: ct);
                resolved = body?.PromptText ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            // Fail open, don't cache the failure — the next call retries the endpoint.
            log?.LogWarning(ex, "Prompt egress lookup failed for '{Key}' — using the built-in fallback", key);
            return fallback;
        }

        cache?.Set(cacheKey, resolved, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration });
        return resolved.Length > 0 ? resolved : fallback;
    }

    private sealed record EgressPrompt(string? PromptKey, string? PromptText, string? ModelId);
}
