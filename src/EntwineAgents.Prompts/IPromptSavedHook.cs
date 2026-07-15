namespace EntwineAgents.Prompts;

/// <summary>
/// Port for reacting to a prompt version being superseded (a new active version saved over a previous one).
/// The open <see cref="PromptService"/> raises it for analysis-enabled prompts; what happens next is the
/// host's business — the private app hooks its premium A/B quality analysis here. The default is a no-op,
/// so the package runs standalone.
/// </summary>
public interface IPromptSavedHook
{
    /// <summary>A new active version replaced <paramref name="previousVersion"/> for an analysis-enabled prompt.</summary>
    Task OnActiveVersionSupersededAsync(string promptKey, int? clientId, int previousVersion, int newVersion);
}

/// <summary>The standalone default: do nothing.</summary>
public sealed class NoopPromptSavedHook : IPromptSavedHook
{
    public Task OnActiveVersionSupersededAsync(string promptKey, int? clientId, int previousVersion, int newVersion)
        => Task.CompletedTask;
}
