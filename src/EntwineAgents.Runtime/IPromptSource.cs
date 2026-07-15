namespace EntwineAgents.Runtime;

/// <summary>
/// The prompt seam an engagement agent fetches its system prompt from, by key. A thin port (like
/// <see cref="IAgentChat"/>) so this domain library stays decoupled from the DB-backed
/// <c>IPromptService</c>: the host binds this to <c>IPromptService</c> (versioned, per-client override) or a
/// seed, while the agent ships a built-in default so it runs standalone and in tests. This is the
/// ENT-329 fix — prompts loaded by key, not compiled-in consts that bypass the loader.
/// </summary>
public interface IPromptSource
{
    /// <summary>Prompt text for <paramref name="key"/>, or <paramref name="fallback"/> if not overridden.</summary>
    Task<string> GetAsync(string key, string fallback, CancellationToken ct = default);
}

/// <summary>The standalone default: always returns the agent's built-in prompt. The host swaps in an
/// <c>IPromptService</c>-backed source to override per client / version.</summary>
public sealed class DefaultPromptSource : IPromptSource
{
    public Task<string> GetAsync(string key, string fallback, CancellationToken ct = default) => Task.FromResult(fallback);
}
