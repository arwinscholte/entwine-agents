namespace EntwineAgents.Runtime;

/// <summary>
/// The open agent shell (ENT-321 #4): the reusable single-turn agent loop as an abstract base. A concrete
/// agent supplies only what actually varies — the overrides that carry the domain IP: which prompt (by key
/// + a built-in fallback), how the input becomes the user message (input shaping + privacy: deciding what NOT
/// to send), how the raw model output becomes a typed / validated / guard-railed result, and what to return
/// when the call fails. The deterministic loop — fetch prompt → one turn → degrade → parse — lives here once,
/// over <see cref="AgentPrimitive"/>.
///
/// This is the 80% case. Agents that need more than one turn (multi-step, tool loops, an explicit
/// request-more cycle) compose <see cref="AgentPrimitive"/> directly instead of deriving from this base.
/// </summary>
public abstract class Agent<TInput, TResult>(IAgentChat chat, IPromptSource? prompts = null)
{
    private readonly IPromptSource _prompts = prompts ?? new DefaultPromptSource();

    /// <summary>Loader key for the system prompt — the host can override / version / seed by this key.</summary>
    protected abstract string Key { get; }

    /// <summary>Built-in system prompt used when the source has no override (the OSS placeholder / default).</summary>
    protected abstract string FallbackPrompt { get; }

    /// <summary>Shape the domain input into the user message — and decide what NOT to send (privacy).</summary>
    protected abstract string BuildUser(TInput input);

    /// <summary>Turn the raw model output into a typed, validated, guard-railed result.</summary>
    protected abstract TResult Parse(string raw);

    /// <summary>The typed "couldn't run" result returned when the model / transport fails.</summary>
    protected abstract TResult OnFailure { get; }

    /// <summary>Force a JSON response. Default true.</summary>
    protected virtual bool Json => true;

    /// <summary>Sampling temperature. Default 0.2.</summary>
    protected virtual double Temperature => 0.2;

    /// <summary>Run the agent: fetch the prompt by key → one LLM turn → degrade to <see cref="OnFailure"/> → parse.</summary>
    public Task<TResult> RunAsync(TInput input, CancellationToken ct = default) =>
        AgentPrimitive.RunAsync(chat, _prompts, Key, FallbackPrompt, BuildUser(input), Parse, OnFailure, Json, Temperature, ct);
}
