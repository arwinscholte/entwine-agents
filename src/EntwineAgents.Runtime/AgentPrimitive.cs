namespace EntwineAgents.Runtime;

/// <summary>
/// The shared agent primitive (ENT-321 #4) — the loop every pillar's agents hand-roll: fetch the system
/// prompt by key (falling back to the agent's built-in default if not overridden), run one LLM turn, degrade
/// gracefully to <c>onFailure</c> if the call throws, then parse the raw output to a typed result.
/// The domain supplies only the three things that actually vary: the prompt, the user text (built from the
/// domain input), and the parse. Everything else — the port calls, the try/catch, the shape — lives here once.
/// </summary>
public static class AgentPrimitive
{
    public static async Task<T> RunAsync<T>(
        IAgentChat chat,
        IPromptSource prompts,
        string promptKey,
        string fallbackSystem,
        string user,
        Func<string, T> parse,
        T onFailure,
        bool json = true,
        double temperature = 0.2,
        CancellationToken ct = default)
    {
        var system = await prompts.GetAsync(promptKey, fallbackSystem, ct);
        string raw;
        try
        {
            raw = await chat.CompleteAsync(new ChatTurn(system, user, json, temperature), ct);
        }
        catch
        {
            return onFailure;   // the model/network failed — the caller's typed "couldn't run" result
        }
        return parse(raw);
    }
}
