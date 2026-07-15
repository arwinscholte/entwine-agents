using Microsoft.Extensions.Caching.Memory;

namespace EntwineAgents.Prompts;

/// <summary>
/// The open prompt-management service: versioned templates with a per-client override and a cached,
/// client→global fallback read path. Persistence is behind <see cref="IPromptRepository"/>; anything that
/// should happen when a version is superseded (e.g. the private A/B quality analysis) hangs off
/// <see cref="IPromptSavedHook"/> — this service knows the WHEN, hosts supply the WHAT.
/// </summary>
public class PromptService : IPromptService
{
    private readonly IPromptRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly IPromptSavedHook _savedHook;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public PromptService(IPromptRepository repo, IMemoryCache cache, IPromptSavedHook? savedHook = null)
    {
        _repo = repo;
        _cache = cache;
        _savedHook = savedHook ?? new NoopPromptSavedHook();
    }

    public async Task<string> GetPromptAsync(string promptKey, int? clientId = null)
    {
        var cacheKey = $"prompt:{promptKey}:{clientId}";

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        // Try client-specific first (highest active version)
        if (clientId.HasValue)
        {
            var clientPrompt = await _repo.GetActiveTextAsync(promptKey, clientId.Value);
            if (clientPrompt != null)
            {
                _cache.Set(cacheKey, clientPrompt, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
                return clientPrompt;
            }
        }

        // Fall back to global default (ClientId = NULL), highest active version
        var globalPrompt = await _repo.GetActiveTextAsync(promptKey, null);

        if (globalPrompt != null)
        {
            _cache.Set(cacheKey, globalPrompt, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
            return globalPrompt;
        }

        return string.Empty;
    }

    public async Task<string?> GetModelOverrideAsync(string promptKey, int? clientId = null)
    {
        var cacheKey = $"prompt-model:{promptKey}:{clientId}";

        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        string? modelId = null;

        if (clientId.HasValue)
            modelId = await _repo.GetActiveModelIdAsync(promptKey, clientId.Value);

        modelId ??= await _repo.GetActiveModelIdAsync(promptKey, null);

        _cache.Set(cacheKey, modelId, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
        return modelId;
    }

    public Task<List<PromptTemplate>> GetAllPromptsAsync(int? clientId = null)
        => _repo.GetAllActiveAsync(clientId);

    public async Task<PromptTemplate> UpsertPromptAsync(PromptTemplate template)
    {
        var (newRow, previousVersion) = await _repo.UpsertVersionAsync(template);

        InvalidateCache(template.PromptKey, template.ClientId);

        // A superseded analysis-enabled version is the host's signal (the private app hooks A/B analysis here).
        if (newRow.IncludeInQualityAnalysis && previousVersion.HasValue)
            await _savedHook.OnActiveVersionSupersededAsync(
                newRow.PromptKey, newRow.ClientId, previousVersion.Value, newRow.Version);

        return newRow;
    }

    public async Task ResetToDefaultAsync(string promptKey, int clientId)
    {
        await _repo.DeactivateClientVersionsAsync(promptKey, clientId);
        InvalidateCache(promptKey, clientId);
    }

    public Task<List<PromptTemplate>> GetHistoryAsync(string promptKey, int? clientId = null)
        => _repo.GetHistoryAsync(promptKey, clientId);

    public async Task<PromptTemplate?> RollbackAsync(string promptKey, int? clientId, int version)
    {
        var rollbackRow = await _repo.RollbackAsync(promptKey, clientId, version);
        if (rollbackRow == null)
            return null;

        InvalidateCache(promptKey, clientId);
        return rollbackRow;
    }

    private void InvalidateCache(string promptKey, int? clientId)
    {
        _cache.Remove($"prompt:{promptKey}:{clientId}");
        _cache.Remove($"prompt-model:{promptKey}:{clientId}");
    }
}
