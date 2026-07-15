namespace EntwineAgents.Ai;

/// <summary>
/// Resolves an <see cref="IChatProvider"/> by routing key (ChatRequest.ProviderKey).
/// A null / unknown key falls back to the default provider, so existing call sites —
/// which never set a key — keep hitting the OpenAI-compatible provider unchanged.
/// </summary>
public interface IChatProviderRegistry
{
    IChatProvider Resolve(string? providerKey);
}
