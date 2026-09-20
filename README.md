# EntwineAgents

[![CI](https://github.com/arwinscholte/entwine-agents/actions/workflows/ci.yml/badge.svg)](https://github.com/arwinscholte/entwine-agents/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/EntwineAgents.Runtime?label=nuget)](https://www.nuget.org/packages/EntwineAgents.Runtime)

A small agent runtime for .NET. You write four overrides — the prompt, how to shape the input, how to parse the
output, what to return when the model fails — and the runtime runs the loop: fetch the prompt, call the model,
parse, or fall back to your typed default. No exceptions leak out of a failed call, no customer name reaches the
model unless you put it there, and a prompt-service outage cannot take an agent down.

It came out of a product where the same loop had been copied into fifteen agents across three services. The loop
is here once. Your prompts, inputs and outputs stay in your code.

## What an agent looks like

```csharp
sealed class TaglineAgent(IAgentChat chat, IPromptSource? prompts = null) : Agent<string, string>(chat, prompts)
{
    protected override string Key => "quickstart.tagline.system";   // hosts can override the prompt by key
    protected override string FallbackPrompt =>
        "You write one short, punchy product tagline. Reply with the tagline only.";
    protected override bool Json => false;
    protected override string BuildUser(string product) => $"Product: {product}";
    protected override string Parse(string raw) => raw.Trim();
    protected override string OnFailure => "(the model was unavailable — try again)";
}
```

`Agent<TInput, TResult>` runs one turn. `BatchAgent<TItem, TResult>` classifies many items: it chunks them, retries a
batch the model mangles, aligns the output to the input by position, and degrades per item rather than dropping the
batch.

Run it:

```
set OPENAI_API_KEY=sk-...        # any OpenAI-compatible endpoint; OPENAI_BASE_URL / OPENAI_MODEL to override
dotnet run --project samples/QuickStart -- "a keyboard for cats"
```

## A sample that does something

`samples/TicketTriage` takes a support-ticket export whose columns are named someone else's way, works out which
column is which, swaps every customer name for a token before the model sees a word, classifies the tickets in
batches into a typed `Triage` per row (category, urgency, next action), puts the names back and prints the table
with the three tickets to look at first. Then run it again with no API key: every row comes back `Unclassified`,
nothing is dropped, and the process still exits 0 — that second run is the whole point of the runtime.

```
dotnet run --project samples/TicketTriage                 # the tickets.csv beside it
dotnet run --project samples/TicketTriage -- yours.csv    # or your own export
```

## Packages

| Package | What it does |
|---|---|
| **EntwineAgents.Runtime** | The agent shells above, the two interfaces they need (`IAgentChat`, `IPromptSource`), and JSON un-fencing for models that wrap their answer in prose. Start here. |
| **EntwineAgents.Ai** | `IChatProvider` + `ChatRequest`: OpenAI-compatible and Anthropic-native providers, a registry that routes by `ChatRequest.ProviderKey`, per-client credentials. Runtime depends on it. |
| **EntwineAgents.Prompts** | Prompts stored as data: versioned templates, a per-client override, cached reads. Storage is an interface. `HttpPromptSource` lets a host with no database load prompts from an endpoint and fall back to the compiled defaults when it is down. |
| **EntwineAgents.Intake** | Messy files into clean tables: XLSX, CSV and OCR text with tolerant header detection; date normalisation; `TableShapeClassifier`, which reads a table for what it is (which of your schemas, which column is which); `PseudonymMap`, which swaps identities for stable session-local tokens before text reaches a model. |
| **EntwineAgents.Ocr** | `IDocumentOcr` with an Azure Document Intelligence implementation: paragraphs, tables as markdown, low-confidence words flagged, per-page positions. |
| **EntwineAgents.Tokenisation** | Tokenisation at rest: deterministic tokens that are stable across runs, with the real values held only as AES-GCM ciphertext in a key store scoped to the tenant (GDPR Art. 4(5) pseudonymisation with a separated key). |

Take the packages you need. Runtime needs Ai; Prompts needs Runtime; the rest stand alone.

## How it is built

**A model failure returns a value, not an exception.** `OnFailure` is typed like the result. A network error, a
timeout or a mangled answer gives your pipeline that value and carries on; a batch degrades one item at a time.

**Prompts load by key, with a compiled fallback.** The host can version a prompt, override it per client or serve it
from a remote endpoint, and if that endpoint is unreachable the agent uses the prompt compiled into it.

**Agents never see the vendor.** Agents call `IAgentChat`; providers implement `IChatProvider`;
`ChatProviderAgentChat` joins the two. Swap OpenAI for Anthropic, or route by request, without touching an agent.

**Identities stay out of the model and out of the database.** `PseudonymMap` replaces names with tokens for the
length of a session; the Tokenisation package keeps stored tokens stable across runs while the real values live
encrypted under a separate key.

**The model's output is typed before anything acts on it.** Deterministic code and people decide what to do with an
agent's answer; nothing in these packages writes a model's opinion anywhere on its own.

## Building

```
dotnet build entwine-agents.slnx
dotnet test  entwine-agents.slnx
```

Requires the .NET 10 SDK.

## License

[Apache-2.0](LICENSE)
