using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;

namespace EntwineAgents.Ocr;

/// <summary>
/// Azure Document Intelligence implementation of <see cref="IDocumentOcr"/> (ENT-321 #3b). Extraction logic
/// lifted verbatim from the Pillar-1 <c>OcrAgent</c>: prebuilt-layout analysis → structured text (paragraphs,
/// tables as markdown, low-confidence words collapsed to signature markers) + per-page polygon data, with a
/// plain-text fallback for known text formats when the service can't analyse the file.
/// </summary>
public sealed class AzureDocumentOcr(DocumentIntelligenceClient client, ILogger<AzureDocumentOcr> logger) : IDocumentOcr
{
    public async Task<OcrExtraction> ExtractAsync(string filePath, CancellationToken ct = default)
    {
        var (text, pages) = await ExtractDocumentTextAsync(filePath, ct);
        return new OcrExtraction(text, pages ?? []);
    }

    private async Task<(string text, List<OcrPageResult>? pages)> ExtractDocumentTextAsync(string filePath, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Extracting text from '{FilePath}' using Azure Document Intelligence", filePath);

            var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
            var binaryData = BinaryData.FromBytes(fileBytes);

            var analyzeOptions = new AnalyzeDocumentOptions("prebuilt-layout", binaryData);
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, analyzeOptions, ct);
            var analyzeResult = operation.Value;

            var text = BuildStructuredText(analyzeResult);
            var ocrPages = BuildOcrPageData(analyzeResult);

            logger.LogInformation("Successfully extracted {CharCount} characters from '{FilePath}'", text.Length, filePath);

            return (text, ocrPages);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract text from '{FilePath}' using Azure Document Intelligence", filePath);

            // Fallback: only read known text-based formats; binary files would produce garbage
            var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".txt", ".csv", ".json", ".xml", ".html", ".md", ".log"
            };
            var extension = Path.GetExtension(filePath);
            if (textExtensions.Contains(extension))
            {
                try
                {
                    logger.LogInformation("Falling back to basic text file reading for '{FilePath}'", filePath);
                    var fallbackText = await File.ReadAllTextAsync(filePath, ct);
                    return (fallbackText, null);
                }
                catch
                {
                    return (string.Empty, null);
                }
            }

            logger.LogWarning(
                "Cannot extract text from binary file '{FilePath}' (extension: {Extension}) — Azure Document Intelligence failed and no text fallback is available",
                filePath, extension);
            return (string.Empty, null);
        }
    }

    /// <summary>
    /// Build structured text from Azure Document Intelligence results, preserving paragraph breaks and
    /// representing tables as markdown. Individual low-confidence words (typically handwritten signatures) are
    /// replaced with [?] so downstream agents don't extract garbled names, while preserving nearby
    /// high-confidence printed text.
    /// </summary>
    private static string BuildStructuredText(AnalyzeResult analyzeResult)
    {
        var sb = new System.Text.StringBuilder();

        var wordConfidenceByPage = BuildWordConfidenceMap(analyzeResult);

        if (analyzeResult.Paragraphs?.Count > 0)
        {
            foreach (var paragraph in analyzeResult.Paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph.Content))
                    continue;

                var pageNumber = paragraph.BoundingRegions?.Count > 0
                    ? paragraph.BoundingRegions[0].PageNumber
                    : 0;
                wordConfidenceByPage.TryGetValue(pageNumber, out var pageWords);

                var cleaned = ReplaceWordsBelowConfidence(paragraph.Content, pageWords);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    sb.AppendLine(cleaned);
                    sb.AppendLine();
                }
            }
        }
        else
        {
            if (analyzeResult.Pages != null)
            {
                foreach (var page in analyzeResult.Pages)
                {
                    wordConfidenceByPage.TryGetValue(page.PageNumber, out var pageWords);

                    foreach (var line in page.Lines)
                    {
                        var cleaned = ReplaceWordsBelowConfidence(line.Content, pageWords);
                        if (!string.IsNullOrWhiteSpace(cleaned))
                            sb.AppendLine(cleaned);
                    }
                    sb.AppendLine();
                }
            }
        }

        if (analyzeResult.Tables?.Count > 0)
        {
            foreach (var table in analyzeResult.Tables)
            {
                sb.AppendLine();
                sb.AppendLine("[TABLE]");

                var maxRow = table.Cells.Max(c => c.RowIndex);
                var maxCol = table.Cells.Max(c => c.ColumnIndex);

                for (int row = 0; row <= maxRow; row++)
                {
                    var cells = new string[maxCol + 1];
                    for (int col = 0; col <= maxCol; col++)
                    {
                        var cell = table.Cells
                            .FirstOrDefault(c => c.RowIndex == row && c.ColumnIndex == col);
                        cells[col] = cell?.Content?.Replace("|", "\\|") ?? "";
                    }
                    sb.AppendLine("| " + string.Join(" | ", cells) + " |");

                    if (row == 0)
                    {
                        sb.AppendLine("| " + string.Join(" | ", cells.Select(_ => "---")) + " |");
                    }
                }
                sb.AppendLine("[/TABLE]");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Word confidence below which a word is treated as handwriting/signature and replaced with [?].</summary>
    private const float LowConfidenceThreshold = 0.50f;

    /// <summary>If a line/paragraph is entirely low-confidence words, collapse it to a single marker.</summary>
    private const string SignatureMarker = "[SIGNATURE/HANDWRITING]";

    private static Dictionary<int, List<(string Content, float Confidence)>> BuildWordConfidenceMap(AnalyzeResult analyzeResult)
    {
        var map = new Dictionary<int, List<(string, float)>>();
        if (analyzeResult.Pages == null) return map;

        foreach (var page in analyzeResult.Pages)
        {
            var words = new List<(string, float)>();
            if (page.Words != null)
            {
                foreach (var word in page.Words)
                    words.Add((word.Content, word.Confidence));
            }
            map[page.PageNumber] = words;
        }
        return map;
    }

    private static string ReplaceWordsBelowConfidence(string textContent, List<(string Content, float Confidence)>? pageWords)
    {
        if (pageWords == null || pageWords.Count == 0)
            return textContent;

        var tokens = textContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return textContent;

        var result = new List<string>();
        var searchStart = 0;
        int lowCount = 0;

        foreach (var token in tokens)
        {
            bool found = false;
            for (int i = searchStart; i < pageWords.Count; i++)
            {
                if (string.Equals(pageWords[i].Content, token, StringComparison.OrdinalIgnoreCase))
                {
                    if (pageWords[i].Confidence < LowConfidenceThreshold)
                    {
                        result.Add("[?]");
                        lowCount++;
                    }
                    else
                    {
                        result.Add(token);
                    }
                    searchStart = i + 1;
                    found = true;
                    break;
                }
            }
            if (!found)
                result.Add(token);
        }

        if (lowCount == tokens.Length)
            return SignatureMarker;

        var collapsed = new List<string>();
        foreach (var item in result)
        {
            if (item == "[?]" && collapsed.Count > 0 && collapsed[^1] == "[?]")
                continue;
            collapsed.Add(item);
        }

        return string.Join(' ', collapsed);
    }

    /// <summary>Extract spatial polygon data for the diagnostic OCR viewer overlay.</summary>
    private static List<OcrPageResult> BuildOcrPageData(AnalyzeResult analyzeResult)
    {
        var pages = new List<OcrPageResult>();

        if (analyzeResult.Pages == null) return pages;

        foreach (var page in analyzeResult.Pages)
        {
            var pageResult = new OcrPageResult
            {
                PageNumber = page.PageNumber,
                Width = page.Width ?? 0,
                Height = page.Height ?? 0,
                Unit = page.Unit?.ToString() ?? "inch"
            };

            if (page.Words != null)
            {
                foreach (var word in page.Words)
                {
                    var wordResult = new OcrWordResult
                    {
                        Content = word.Content,
                        Confidence = word.Confidence
                    };
                    if (word.Polygon != null)
                        wordResult.Polygon = word.Polygon.ToList();
                    pageResult.Words.Add(wordResult);
                }
            }

            if (page.Lines != null)
            {
                foreach (var line in page.Lines)
                {
                    var lineResult = new OcrLineResult
                    {
                        Content = line.Content
                    };
                    if (line.Polygon != null)
                        lineResult.Polygon = line.Polygon.ToList();
                    pageResult.Lines.Add(lineResult);
                }
            }

            pages.Add(pageResult);
        }

        if (analyzeResult.Paragraphs != null)
        {
            foreach (var paragraph in analyzeResult.Paragraphs)
            {
                if (paragraph.BoundingRegions == null) continue;

                foreach (var region in paragraph.BoundingRegions)
                {
                    var targetPage = pages.FirstOrDefault(p => p.PageNumber == region.PageNumber);
                    if (targetPage == null) continue;

                    var paraResult = new OcrParagraphResult
                    {
                        Content = paragraph.Content
                    };
                    if (region.Polygon != null)
                        paraResult.Polygon = region.Polygon.ToList();
                    targetPage.Paragraphs.Add(paraResult);
                }
            }
        }

        return pages;
    }
}
