namespace EntwineAgents.Ocr;

/// <summary>
/// The result of an OCR extraction: the structured, downstream-ready text plus per-page spatial data
/// (words/lines/paragraphs + polygons) for diagnostic overlays. <see cref="Pages"/> may be empty when the
/// source is a plain-text fallback with no layout information.
/// </summary>
public sealed record OcrExtraction(string Text, IReadOnlyList<OcrPageResult> Pages);

public class OcrPageResult
{
    public int PageNumber { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Unit { get; set; } = "inch";
    public List<OcrWordResult> Words { get; set; } = new();
    public List<OcrLineResult> Lines { get; set; } = new();
    public List<OcrParagraphResult> Paragraphs { get; set; } = new();
}

public class OcrWordResult
{
    public string Content { get; set; } = "";
    public double Confidence { get; set; }
    public List<float> Polygon { get; set; } = new();
}

public class OcrLineResult
{
    public string Content { get; set; } = "";
    public List<float> Polygon { get; set; } = new();
}

public class OcrParagraphResult
{
    public string Content { get; set; } = "";
    public List<float> Polygon { get; set; } = new();
}
