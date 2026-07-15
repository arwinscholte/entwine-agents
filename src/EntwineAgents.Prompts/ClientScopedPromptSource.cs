using EntwineAgents.Runtime;

namespace EntwineAgents.Prompts;

/// <summary>
/// Adapts the monolith's rich <see cref="IPromptService"/> (versioned, per-client, DB-backed) to the shared
/// <see cref="IPromptSource"/> port (ENT-321 #5d), scoped to a fixed clientId. Lets monolith code that runs
/// outside an HTTP tenant scope — e.g. the <c>VisionAgent</c> MassTransit consumer, which carries the clientId
/// on the message — fetch prompts through the same port the concierge classifiers and engagement agents use,
/// with the per-client override intact. An empty stored prompt falls back to the caller's built-in default.
/// </summary>
public sealed class ClientScopedPromptSource(IPromptService prompts, int? clientId) : IPromptSource
{
    public async Task<string> GetAsync(string key, string fallback, CancellationToken ct = default)
    {
        var text = await prompts.GetPromptAsync(key, clientId);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }
}
