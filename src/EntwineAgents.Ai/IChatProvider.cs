namespace EntwineAgents.Ai;

/// <summary>
/// A single chat-completion request, provider-agnostic. Carries the shape the agents send
/// (system/user messages, optional JSON response, temperature, optional per-call model override),
/// the routing <see cref="ProviderKey"/>, and the <see cref="ClientId"/> used to resolve a
/// per-client BYOK credential at the provider (ENT-352).
/// </summary>
public sealed record ChatRequest(
    string UserPrompt,
    string? SystemPrompt = null,
    string? Model = null,          // null → provider default (LlmOptions.ModelId / the client credential's ModelId)
    double Temperature = 0.0,
    bool JsonResponse = false,
    int? MaxTokens = null,          // null → omit (provider/model default)
    string? ProviderKey = null,    // null → default (OpenAI-compatible) provider
    int? ClientId = null);         // ENT-352: resolves this client's BYOK credential (null → platform key)

/// <summary>
/// Abstraction over a chat-completion provider. Extracting this from the ~10 inline
/// chat/completions call sites is the seam the rest of the provider framework builds on.
/// </summary>
public interface IChatProvider
{
    string Name { get; }
    Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
