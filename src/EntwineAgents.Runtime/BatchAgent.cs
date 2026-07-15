using Microsoft.Extensions.Logging;

namespace EntwineAgents.Runtime;

/// <summary>
/// The open agent shell for BATCHED classification (ENT-321) — the sibling of <see cref="Agent{TInput,TResult}"/>
/// for agents that process many items in chunks with a per-batch retry (the concierge classifiers' pattern).
/// A concrete agent supplies only the domain IP: which prompt (by key + fallback), how a batch becomes the user
/// message, how the raw output parses to one result per item (position-aligned), and the per-item degrade when a
/// batch can't be parsed after retries. The chunking, prompt fetch, retry loop, and degrade live here once.
/// Consumes the agent seam (<see cref="IAgentChat"/> — the API agents talk to); hosts adapt the provider seam
/// (IChatProvider, the SPI providers implement) via <see cref="ChatProviderAgentChat"/>.
/// </summary>
public abstract class BatchAgent<TItem, TResult>(IAgentChat chat, IPromptSource? prompts = null, ILogger? log = null)
{
    private readonly IPromptSource _prompts = prompts ?? new DefaultPromptSource();

    /// <summary>Optional logger for retry/degrade diagnostics — also available to subclasses' parse helpers.</summary>
    protected ILogger? Log { get; } = log;

    /// <summary>Loader key for the system prompt — the host can override / version / seed by this key.</summary>
    protected abstract string Key { get; }

    /// <summary>Built-in system prompt used when the source has no override (the OSS placeholder / default).</summary>
    protected abstract string FallbackPrompt { get; }

    /// <summary>Items per model call. Classification is independent per item, so cross-batch order is irrelevant.</summary>
    protected virtual int BatchSize => 12;

    /// <summary>Attempts per batch before degrading — covers both a failed call and unparseable output.</summary>
    protected virtual int MaxAttempts => 2;

    /// <summary>Sampling temperature. Default 0.0 (deterministic classification).</summary>
    protected virtual double Temperature => 0.0;

    /// <summary>Render one batch of items into the user message.</summary>
    protected abstract string BuildUserPrompt(IReadOnlyList<TItem> batch);

    /// <summary>Parse the raw output into one result per input item (position-aligned); false → retry the batch.</summary>
    protected abstract bool TryParse(string raw, IReadOnlyList<TItem> batch, out List<TResult> parsed);

    /// <summary>The degrade result for one item when its batch can't be parsed after <see cref="MaxAttempts"/>.</summary>
    protected abstract TResult Flagged(TItem item);

    /// <summary>Classify every item, batched. Order is preserved across batches.</summary>
    public async Task<IReadOnlyList<TResult>> RunAsync(IReadOnlyList<TItem> items, CancellationToken ct = default)
    {
        var results = new List<TResult>(items.Count);
        for (var start = 0; start < items.Count; start += BatchSize)
            results.AddRange(await RunBatchAsync(items.Skip(start).Take(BatchSize).ToList(), ct));
        return results;
    }

    private async Task<List<TResult>> RunBatchAsync(List<TItem> batch, CancellationToken ct)
    {
        var user = BuildUserPrompt(batch);
        var system = await _prompts.GetAsync(Key, FallbackPrompt, ct);
        var name = GetType().Name;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string raw;
            try
            {
                raw = await chat.CompleteAsync(
                    new ChatTurn(system, user, Json: true, Temperature: Temperature), ct);
            }
            catch (Exception ex)
            {
                Log?.LogWarning(ex, "{Agent} batch call failed (attempt {Attempt}/{Max})", name, attempt, MaxAttempts);
                continue;
            }

            if (TryParse(raw, batch, out var parsed))
                return parsed;

            Log?.LogWarning("{Agent} batch output invalid (attempt {Attempt}/{Max})", name, attempt, MaxAttempts);
        }

        Log?.LogWarning("{Agent} degrading {Count} items to the fallback after retry", name, batch.Count);
        return batch.Select(Flagged).ToList();
    }
}
