# EntwineAgents

A lean, composable **agent runtime for .NET** — the shared substrate for building LLM agents that behave like
software components: typed inputs and outputs, prompts managed as data, privacy by construction, and graceful
degradation everywhere.

Extracted from a production system where the same agent loop had been hand-rolled fifteen times across three
products. The loop lives here once; what varies stays yours.

## The idea

> **An agent = a prompt (loaded by key, host-overridable) + input shaping + typed parsing + graceful degrade**,
> riding a shell that owns the loop.

```csharp
sealed class TaglineAgent(IAgentChat chat, IPromptSource? prompts = null) : Agent<string, string>(chat, prompts)
{
    protected override string Key => "quickstart.tagline.system";   // hosts can override by key
    protected override string FallbackPrompt =>
        "You write one short, punchy product tagline. Reply with the tagline only.";
    protected override bool Json => false;
    protected override string BuildUser(string product) => $"Product: {product}";
    protected override string Parse(string raw) => raw.Trim();
    protected override string OnFailure => "(the model was unavailable — try again)";
}
```

Four overrides carry everything that is domain-specific. The shell owns the loop: fetch the prompt by key
(falling back to the built-in), run one turn, degrade to a **typed** failure value if the model or network
fails, parse to a **typed** result. There is also a batched sibling, `BatchAgent<TItem, TResult>`, for
classify-many-items work: chunking, per-batch retry, position-aligned parsing, and per-item degrade.

Run it:

```bash
set OPENAI_API_KEY=sk-...        # any OpenAI-compatible endpoint; OPENAI_BASE_URL / OPENAI_MODEL to override
dotnet run --project samples/QuickStart -- "a keyboard for cats"
```

## Packages

| Package | What it gives you |
|---|---|
| **EntwineAgents.Ai** | The provider seam: `IChatProvider` + `ChatRequest`, OpenAI-compatible and Anthropic-native providers, a routing registry (`ChatRequest.ProviderKey`), typed options. |
| **EntwineAgents.Runtime** | The agent shells (`Agent<TInput,TResult>`, `BatchAgent<TItem,TResult>`), the ports agents consume (`IAgentChat`, `IPromptSource`), JSON un-fencing, and the bridge between the provider and agent seams. |
| **EntwineAgents.Prompts** | Prompt management: versioned templates, per-client override, cached client→global fallback reads. Persistence is a port (`IPromptRepository`); post-save behaviour is a port (`IPromptSavedHook`); `HttpPromptSource` binds database-free hosts over a prompt-egress endpoint — failing open to built-in fallbacks. |
| **EntwineAgents.Intake** | Turn messy sources into clean tables: XLSX/CSV/OCR-text reading with tolerant header detection, date normalisation, and `PseudonymMap` — typed, stable, session-local pseudonymisation so identities never reach a model. |
| **EntwineAgents.Ocr** | Document OCR behind a port: `IDocumentOcr` with an Azure Document Intelligence implementation — structured text (paragraphs, tables as markdown, low-confidence word handling) plus per-page spatial data. |
| **EntwineAgents.Tokenisation** | Tokenisation at rest (pseudonymisation with a separated key, GDPR Art. 4(5)): deterministic cross-run-stable tokens; real values live only as AES-GCM ciphertext in a tenant-scoped key store. |

Each package stands alone; take what you need. `Runtime` depends on `Ai`; `Prompts` depends on `Runtime`;
everything else is independent.

## Design principles

- **Two seams, bridged once.** `IChatProvider` is the SPI providers implement; `IAgentChat` is the API agents
  consume; `ChatProviderAgentChat` connects them. Your agents never know which model or vendor is behind them.
- **Agents propose; algorithms compute; humans decide.** The shells make agent output typed and validated so
  deterministic code — and people — can act on it. Nothing here auto-persists a model's opinion.
- **Prompts are data.** Loaded by key with a compiled fallback, so prompts can be versioned, per-client
  overridden, and served remotely — and a prompt-service outage can never take an agent down.
- **Fail open, degrade typed.** A failed call returns your `OnFailure` value, not an exception across your
  pipeline; a batch the model mangles degrades per-item, never by discarding the batch.
- **Privacy by construction.** Session-local pseudonyms for what reaches a model; separated-key tokenisation
  for what reaches a database.

## Building

```bash
dotnet build entwine-agents.slnx
dotnet test  entwine-agents.slnx
```

Requires the .NET 10 SDK.

## License

[Apache-2.0](LICENSE)
