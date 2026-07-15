namespace EntwineAgents.Ai;

/// <summary>
/// A single chat-completion request, provider-agnostic. Phase 1.0 carries exactly the
/// shape the agents already send (system/user messages, optional JSON response, temperature,
/// optional per-call model override). ProviderKey is reserved for Phase 1.1 (multi-provider).
/// </summary>
public sealed record ChatRequest(
    string UserPrompt,
    string? SystemPrompt = null,
    string? Model = null,          // null → provider default (LlmOptions.ModelId)
    double Temperature = 0.0,
    bool JsonResponse = false,
    int? MaxTokens = null,          // null → omit (provider/model default)
    string? ProviderKey = null);   // reserved (Phase 1.1)

/// <summary>
/// Abstraction over a chat-completion provider. Extracting this from the ~10 inline
/// chat/completions call sites is the seam the rest of the provider framework builds on.
/// </summary>
public interface IChatProvider
{
    string Name { get; }
    Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
