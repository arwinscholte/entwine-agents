namespace EntwineAgents.Ai;

/// <summary>
/// The default <see cref="IChatProvider"/> injected into agents. Delegates to the
/// registry-resolved provider based on <see cref="ChatRequest.ProviderKey"/>, so any
/// call site can opt into a specific provider per call without depending on the registry.
/// A null key routes to the default (OpenAI-compatible) provider — behaviour-identical
/// to Phase 1.0 for every existing caller.
/// </summary>
public sealed class RoutingChatProvider : IChatProvider
{
    private readonly IChatProviderRegistry _registry;

    public RoutingChatProvider(IChatProviderRegistry registry) => _registry = registry;

    public string Name => "router";

    public Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => _registry.Resolve(request.ProviderKey).CompleteAsync(request, cancellationToken);
}
