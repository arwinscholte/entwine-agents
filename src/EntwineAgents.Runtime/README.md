# EntwineAgents.Runtime

Typed agent shells for .NET. **An agent = a prompt (loaded by key, host-overridable) + input shaping +
typed parsing + graceful degrade** — four overrides carry everything that is yours; the shell owns the loop.

```csharp
sealed class TaglineAgent(IAgentChat chat, IPromptSource? prompts = null) : Agent<string, string>(chat, prompts)
{
    protected override string Key => "quickstart.tagline.system";
    protected override string FallbackPrompt => "You write one short, punchy product tagline.";
    protected override bool Json => false;
    protected override string BuildUser(string product) => $"Product: {product}";
    protected override string Parse(string raw) => raw.Trim();
    protected override string OnFailure => "(the model was unavailable)";
}
```

Also included:

- `BatchAgent<TItem, TResult>` — classify many items: chunking, per-batch retry, position-aligned parsing,
  per-item degrade (a mangled batch never discards your items).
- `IAgentChat` / `IPromptSource` — the ports agents consume, with standalone defaults.
- `ChatProviderAgentChat` — bridges any `EntwineAgents.Ai` provider to the agent seam.
- `JsonText.Unfence` — strips the markdown fences models love to wrap JSON in.

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET.
