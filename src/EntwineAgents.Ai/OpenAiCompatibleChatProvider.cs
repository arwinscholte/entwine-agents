using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EntwineAgents.Ai;

/// <summary>
/// Chat provider for any OpenAI-compatible endpoint (OpenAI, OpenRouter, Groq, Ollama, …).
/// Wraps the existing named "LLM" HttpClient + LlmOptions — behaviour-identical to the inline
/// chat/completions calls it replaces: same endpoint, body shape, success check and parse.
/// </summary>
public sealed class OpenAiCompatibleChatProvider : IChatProvider
{
    /// <summary>Routing key used by <see cref="ChatProviderRegistry"/> / ChatRequest.ProviderKey.</summary>
    public const string ProviderKey = "openai";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _options;
    private readonly ICredentialStore? _credentialStore;

    /// <param name="credentialStore">
    /// Optional (ENT-352). When registered, a request carrying a <see cref="ChatRequest.ClientId"/> with an
    /// active credential for this provider uses that client's key/endpoint/model; otherwise the platform key.
    /// Left null (the default) → platform-key-only behaviour, unchanged for consumers that don't register a store.
    /// </param>
    public OpenAiCompatibleChatProvider(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options, ICredentialStore? credentialStore = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _credentialStore = credentialStore;
    }

    public string Name => "OpenAI-compatible";

    public async Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("LLM");

        // ENT-352: resolve this client's BYOK credential (null → platform key on the named client's endpoint).
        var cred = await ResolveClientCredentialAsync(_credentialStore, request.ClientId, ProviderKey, cancellationToken);

        var messages = new List<object>(2);
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.UserPrompt });

        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model ?? cred?.ModelId ?? _options.ModelId,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
        };
        if (request.JsonResponse)
            body["response_format"] = new { type = "json_object" };
        if (request.MaxTokens is int maxTokens)
            body["max_tokens"] = maxTokens;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ResolveRequestUri(cred, "chat/completions"))
        {
            Content = JsonContent.Create(body),
        };
        // BYOK → override the named client's baked platform Authorization with the client's key. Platform path
        // leaves the request header unset so the client default applies (behaviour-identical to before).
        if (cred is not null)
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cred.ApiKey}");

        var response = await client.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return ExtractContent(doc);
    }

    /// <summary>Resolves the active per-client credential for a provider, or null (platform fallback).</summary>
    internal static async Task<ProviderCredentialDto?> ResolveClientCredentialAsync(
        ICredentialStore? store, int? clientId, string providerKey, CancellationToken cancellationToken)
    {
        if (store is null || clientId is not int cid) return null;
        var cred = await store.GetSecretAsync(cid, providerKey, cancellationToken);
        return cred is { IsActive: true } && !string.IsNullOrWhiteSpace(cred.ApiKey) ? cred : null;
    }

    /// <summary>
    /// BYOK with its own endpoint → absolute URI against the credential's BaseUrl. Otherwise a relative path
    /// resolved against the named client's BaseAddress (platform endpoint, or a BYOK key on that same endpoint).
    /// </summary>
    private static Uri ResolveRequestUri(ProviderCredentialDto? cred, string relativePath)
        => string.IsNullOrWhiteSpace(cred?.BaseUrl)
            ? new Uri(relativePath, UriKind.Relative)
            : new Uri(new Uri(cred!.BaseUrl!.TrimEnd('/') + "/"), relativePath);

    /// <summary>content is usually a string; tolerate array-of-parts / null shapes too. Shared with the Azure provider.</summary>
    internal static string ExtractContent(JsonElement doc)
    {
        if (!doc.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return string.Empty;
        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
            return sb.ToString();
        }

        return string.Empty;
    }
}
