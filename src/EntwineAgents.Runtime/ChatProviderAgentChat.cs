using EntwineAgents.Ai;

namespace EntwineAgents.Runtime;

/// <summary>
/// Bridges the Pillar-1 provider seam (<see cref="IChatProvider"/>, EntwineAgents.Ai) to the agent-facing
/// <see cref="IAgentChat"/> — the unification ENT-321 set out to make. A host registers this so the engagement
/// agents (and any runtime consumer) run on the same routed, credentialed providers as the rest of the
/// platform, with no second LLM integration. A <see cref="ChatTurn"/> maps 1:1 onto a <see cref="ChatRequest"/>.
/// </summary>
public sealed class ChatProviderAgentChat(IChatProvider provider) : IAgentChat
{
    public Task<string> CompleteAsync(ChatTurn turn, CancellationToken ct = default) =>
        provider.CompleteAsync(
            new ChatRequest(turn.User, turn.System, Temperature: turn.Temperature, JsonResponse: turn.Json), ct);
}
