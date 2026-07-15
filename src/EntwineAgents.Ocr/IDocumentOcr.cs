namespace EntwineAgents.Ocr;

/// <summary>
/// Commodity OCR seam (ENT-321 #3b): extract downstream-ready text (+ optional spatial data) from a document.
/// Any pillar depends on this port; the concierge (Pillar 2) uses it to read a PDF then tokenise the text, the
/// Pillar-1 pipeline consumes it in <c>OcrAgent</c>. The Azure Document Intelligence implementation is the
/// reference; the port keeps callers off the SDK.
/// </summary>
public interface IDocumentOcr
{
    /// <summary>Extract text (+ per-page spatial data) from the file at <paramref name="filePath"/>.</summary>
    Task<OcrExtraction> ExtractAsync(string filePath, CancellationToken ct = default);
}
