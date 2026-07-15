# EntwineAgents.Intake

Turn messy sources into clean, model-safe input.

- `RecordTableReader` — one `Table` abstraction over **XLSX**, **CSV**, and **OCR'd document text**
  (parses the `[TABLE]` markdown emitted by `EntwineAgents.Ocr`), with tolerant header detection
  (banner rows above the real header are fine) and case/space-insensitive column lookup.
- `DateNormalizer` — messy real-world date columns to `DateOnly` + active/flag signals.
- `PseudonymMap` — **session-local pseudonymisation**: typed, stable tokens (`ACCOUNT_01`, `PARTNER_02`, ...)
  replace real identities before text reaches an LLM, and hydrate back for the human-facing output. The map
  lives only in memory and dies with the session.

```csharp
var map = new PseudonymMap();
var safe = map.Scrub("Acme Corp renewal at Globex");   // identities out
var text = map.Hydrate(modelOutput);                    // identities back, for humans only
```

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET.
