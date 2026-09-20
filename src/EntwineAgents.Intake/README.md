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

- `TableShapeClassifier` — **read a table for what it is.** Give it your target schemas (name, columns with
  synonyms, optional value-shape predicates) and it binds each schema's columns to the table's headers —
  exact name, synonym, whole-word containment, then the values' shape — and scores every schema; the best
  complete reading wins. That path is deterministic and free. Only the residue (nothing fits, or a tie) goes
  to a model through a `ShapeResidueCall` you supply, with the headers and a few sample rows you scrub; the
  answer is validated against your schemas and the table's headers, so it cannot invent a column or a header.
  `ColumnMap.Project(table)` re-heads the table with your canonical names; `Describe()` is the
  "Partner = Reseller · Customer = Account name" line to show a person before anything runs on it.

```csharp
var engagements = new TargetSchema("Engagements", new[]
{
    SchemaColumn.Of("Partner",  required: true, "Partner name", "Reseller"),
    SchemaColumn.Of("Customer", required: true, "Account", "Client"),
    new SchemaColumn("Start", new[] { "Start date", "Kick-off" }, ValueShape: LooksLikeDates),
}, "one row per piece of partner work on a customer");

var shape = new TableShapeClassifier(new[] { engagements, sourcing, outcomes });
var result = await shape.ClassifyAsync(table,
    residue: (prompt, ct) => chat.CompleteAsync(prompt, ct),   // any chat client; null = deterministic only
    scrub: map.Scrub);                                          // sample rows leave pseudonymised
if (!result.IsUnknown)
    Console.WriteLine($"Read as {result.Best.Schema.Name}: {result.Best.Describe()}");
var canonical = result.Best?.Project(table);                    // headers are now your names
```

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET.
