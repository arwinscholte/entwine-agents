namespace EntwineAgents.Runtime;

/// <summary>One LLM turn: a system + user prompt, whether to force JSON, and a temperature.</summary>
public sealed record ChatTurn(string System, string User, bool Json = true, double Temperature = 0.2);

/// <summary>
/// The agent-facing LLM seam of the shared runtime (ENT-321 #4). Agents depend on this thin turn-based port
/// rather than the lower-level provider seam; <see cref="ChatProviderAgentChat"/> bridges it to the Pillar-1
/// <c>IChatProvider</c> (EntwineAgents.Ai), so there is one provider seam and one agent seam, unified — not
/// two parallel abstractions in different assemblies.
/// </summary>
public interface IAgentChat
{
    Task<string> CompleteAsync(ChatTurn turn, CancellationToken ct = default);
}
