# EntwineAgents.Ocr

Document OCR behind a port.

- `IDocumentOcr` — one call: file in, `OcrExtraction` out (structured text + per-page spatial data).
- `AzureDocumentOcr` — the Azure Document Intelligence implementation: paragraphs preserved, **tables
  rendered as markdown** between `[TABLE]` markers (ready for `EntwineAgents.Intake` to parse), individual
  low-confidence words replaced with `[?]` so downstream extraction never ingests garbled handwriting, and
  word/line/paragraph polygons for viewer overlays.

```csharp
IDocumentOcr ocr = new AzureDocumentOcr(documentIntelligenceClient, logger);
var extraction = await ocr.ExtractAsync("contract.pdf");
// extraction.Text  -> downstream-ready text
// extraction.Pages -> polygons for a diagnostic overlay
```

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET.
