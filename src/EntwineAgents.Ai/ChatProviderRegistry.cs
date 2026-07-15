namespace EntwineAgents.Ai;

/// <summary>
/// Maps routing keys to provider instances. Keys are case-insensitive. The default
/// provider (OpenAI-compatible) is returned for null / unknown keys, preserving the
/// pre-routing behaviour of every call site that doesn't specify a provider.
/// </summary>
public sealed class ChatProviderRegistry : IChatProviderRegistry
{
    private readonly Dictionary<string, IChatProvider> _byKey;
    private readonly IChatProvider _default;

    public ChatProviderRegistry(OpenAiCompatibleChatProvider openAi, AnthropicChatProvider anthropic)
    {
        _byKey = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase)
        {
            [OpenAiCompatibleChatProvider.ProviderKey] = openAi,
            [AnthropicChatProvider.ProviderKey] = anthropic,
        };
        _default = openAi;
    }

    public IChatProvider Resolve(string? providerKey)
        => !string.IsNullOrWhiteSpace(providerKey) && _byKey.TryGetValue(providerKey, out var provider)
            ? provider
            : _default;
}
