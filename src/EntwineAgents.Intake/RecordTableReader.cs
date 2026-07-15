using ClosedXML.Excel;

namespace EntwineAgents.Intake;

/// <summary>
/// Reads a partner-engagement table from XLSX or CSV into header-mapped string rows. Locates the header
/// row (the first row that contains a recognisable "Engagement" and "Customer" column) so title/banner
/// rows above it are tolerated. Column lookups are case-insensitive and space-insensitive.
/// </summary>
public static class RecordTableReader
{
    public sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
    {
        private readonly Dictionary<string, int> _index = BuildIndex(Headers);

        public string Cell(IReadOnlyList<string> row, params string[] names)
        {
            foreach (var n in names)
                if (_index.TryGetValue(Norm(n), out var i) && i < row.Count)
                    return row[i];
            return string.Empty;
        }

        public bool HasColumn(params string[] names) => names.Any(n => _index.ContainsKey(Norm(n)));

        private static Dictionary<string, int> BuildIndex(IReadOnlyList<string> headers)
        {
            var d = new Dictionary<string, int>();
            for (var i = 0; i < headers.Count; i++)
            {
                var key = Norm(headers[i]);
                if (key.Length > 0 && !d.ContainsKey(key)) d[key] = i;
            }
            return d;
        }
    }

    private static string Norm(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    public static Table ReadXlsx(Stream stream, string? sheetName = null)
    {
        using var wb = new XLWorkbook(stream);
        var ws = sheetName is not null
            ? wb.Worksheets.FirstOrDefault(w => string.Equals(w.Name, sheetName, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException($"Sheet '{sheetName}' not found.")
            : wb.Worksheets.First();

        var raw = ws.RowsUsed()
            .Select(r => r.Cells(1, r.LastCellUsed()?.Address.ColumnNumber ?? 1)
                          .Select(c => c.GetString().Trim()).ToList())
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return FromRawRows(raw);
    }

    /// <summary>
    /// Reads a table out of OCR'd document text (the PDF intake path). EntwineAgents.Ocr renders detected
    /// tables as markdown between <c>[TABLE]</c>/<c>[/TABLE]</c> markers; this parses every block into raw
    /// rows — skipping markdown separator rows and page-repeated header rows — and reuses the same header
    /// detection as XLSX/CSV. No blocks → an empty table (intake reports "needs data" as usual).
    /// </summary>
    public static Table ReadOcrText(string ocrText)
    {
        var raw = new List<IReadOnlyList<string>>();
        IReadOnlyList<string>? firstRow = null;
        var inTable = false;

        foreach (var line in ocrText.Split('\n'))
        {
            var t = line.Trim();
            if (t.Equals("[TABLE]", StringComparison.OrdinalIgnoreCase)) { inTable = true; continue; }
            if (t.Equals("[/TABLE]", StringComparison.OrdinalIgnoreCase)) { inTable = false; continue; }
            if (!inTable || !t.StartsWith('|')) continue;

            var cells = t.Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':'))) continue;   // markdown separator

            // A table split across PDF pages repeats its header row per page — keep only the first.
            if (firstRow is null) firstRow = cells;
            else if (cells.SequenceEqual(firstRow, StringComparer.OrdinalIgnoreCase)) continue;

            raw.Add(cells);
        }

        return FromRawRows(raw);
    }

    public static Table ReadCsv(TextReader reader)
    {
        var raw = new List<IReadOnlyList<string>>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            raw.Add(SplitCsv(line));
        return FromRawRows(raw);
    }

    private static Table FromRawRows(List<IReadOnlyList<string>> raw)
    {
        if (raw.Count == 0)
            return new Table([], []);

        // Header row = the first row that names an engagement/deal/description column (covers delivery
        // engagement exports and PRM/CRM pipeline exports alike), so banner rows above it are tolerated.
        string[] anchors = ["engagement", "engagementdescription", "description", "deal", "opportunity"];
        var headerIdx = 0;
        for (var i = 0; i < raw.Count; i++)
        {
            var normed = raw[i].Select(Norm).ToHashSet();
            if (anchors.Any(normed.Contains))
            {
                headerIdx = i;
                break;
            }
        }

        var headers = raw[headerIdx];
        var rows = raw.Skip(headerIdx + 1).Where(r => r.Any(c => c.Length > 0)).ToList();
        return new Table(headers, rows);
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) { result.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }
}
