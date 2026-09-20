using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EntwineAgents.Intake;

/// <summary>One column a <see cref="TargetSchema"/> wants: its canonical name, the header names it goes by, and
/// optionally what its values look like (a predicate over a sample of the column's non-empty values).</summary>
/// <param name="Name">The canonical header the consumer reads.</param>
/// <param name="Synonyms">Header names that mean this column (matched whitespace- and case-insensitively; a whole-word
/// containment counts at lower confidence).</param>
/// <param name="Required">A schema without this column mapped is not that schema.</param>
/// <param name="ValueShape">Optional: true when a sample of values looks right (dates, a 0–10 rating, a status vocabulary).</param>
public sealed record SchemaColumn(
    string Name,
    IReadOnlyList<string> Synonyms,
    bool Required = false,
    Func<IReadOnlyList<string>, bool>? ValueShape = null)
{
    public static SchemaColumn Of(string name, bool required, params string[] synonyms) => new(name, synonyms, required);
}

/// <summary>What a table might be: a name, its columns, and a one-line description a model can read.</summary>
public sealed record TargetSchema(string Name, IReadOnlyList<SchemaColumn> Columns, string? Description = null)
{
    public SchemaColumn? Column(string name) => Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One canonical column bound to one header of the table, with how sure and why.</summary>
/// <param name="Column">The schema's canonical column.</param>
/// <param name="Header">The table header it is bound to, as written.</param>
/// <param name="Confidence">0–1.</param>
/// <param name="Basis">"exact" · "synonym" · "contains" · "shape" · "model".</param>
public sealed record ColumnMapping(string Column, string Header, double Confidence, string Basis);

/// <summary>A table read as one schema: the bindings, what is still missing, and a score in 0–1.</summary>
public sealed record ColumnMap(TargetSchema Schema, IReadOnlyList<ColumnMapping> Mappings, double Score)
{
    public string? HeaderFor(string column) =>
        Mappings.FirstOrDefault(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase))?.Header;

    public bool Has(string column) => HeaderFor(column) is not null;

    /// <summary>Required columns with no header bound.</summary>
    public IReadOnlyList<string> MissingRequired =>
        Schema.Columns.Where(c => c.Required && !Has(c.Name)).Select(c => c.Name).ToList();

    public bool Complete => MissingRequired.Count == 0;

    /// <summary>Every required column is named by a header (exact, synonym, containment or the model) — not
    /// inferred from its values alone. A required column read only from value shape is a weaker claim, and a
    /// schema that needs it ranks below one whose headers say what they are.</summary>
    public bool RequiredBoundByHeader =>
        Schema.Columns.Where(c => c.Required).All(c =>
            Mappings.Any(m => string.Equals(m.Column, c.Name, StringComparison.OrdinalIgnoreCase) && m.Basis != "shape"));

    /// <summary>The table re-headed with the canonical names for every bound column; unbound headers keep their own
    /// name. Consumers that read by canonical header then work unchanged.</summary>
    public RecordTableReader.Table Project(RecordTableReader.Table table)
    {
        var byHeader = Mappings.ToDictionary(m => TableShapeClassifier.Norm(m.Header), m => m.Column);
        var headers = table.Headers.Select(h => byHeader.TryGetValue(TableShapeClassifier.Norm(h), out var c) ? c : h).ToList();
        return new RecordTableReader.Table(headers, table.Rows);
    }

    /// <summary>"Partner = Reseller · Customer = Account name · …" — the line a person confirms before anything runs on it.</summary>
    public string Describe(string separator = " · ") =>
        string.Join(separator, Mappings.OrderBy(m => IndexIn(Schema, m.Column)).Select(m => $"{m.Column} = {m.Header}"));

    private static int IndexIn(TargetSchema s, string column)
    {
        for (var i = 0; i < s.Columns.Count; i++)
            if (string.Equals(s.Columns[i].Name, column, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}

/// <summary>The classifier's answer: the best reading (null = none fits), every candidate scored, and whether a
/// model call was needed to get there.</summary>
public sealed record ShapeResult(ColumnMap? Best, IReadOnlyList<ColumnMap> Candidates, bool UsedModel, string? Note)
{
    public bool IsUnknown => Best is null;
}

/// <summary>The model seam: given a prompt, return the raw answer (or null when unavailable). Adapt whatever chat
/// client you have; the classifier never sees a provider.</summary>
public delegate Task<string?> ShapeResidueCall(string prompt, CancellationToken cancellationToken);

public sealed record TableShapeOptions
{
    /// <summary>A candidate scoring below this is not offered as the best reading.</summary>
    public double MinScore { get; init; } = 0.35;
    /// <summary>Two candidates within this of each other are a tie — the residue call (when given) decides.</summary>
    public double TieMargin { get; init; } = 0.05;
    /// <summary>Rows shown to the residue call (after the caller's scrub).</summary>
    public int SampleRows { get; init; } = 5;
    /// <summary>Non-empty values a value-shape predicate sees.</summary>
    public int ShapeSample { get; init; } = 25;
    /// <summary>Confidence given to a binding the model supplied.</summary>
    public double ModelConfidence { get; init; } = 0.6;
}

/// <summary>
/// Reads a table for what it is. Given target schemas (what a table could be), binds each schema's columns to the
/// table's headers — exact name, synonym, whole-word containment, then the values' shape — and scores every
/// schema; the best complete reading wins. That path is deterministic and free. Only the residue — no schema
/// fits, or two tie — goes to a model, through the caller's <see cref="ShapeResidueCall"/>, with the headers and
/// a few sample rows the caller has scrubbed; the answer is validated against the schemas and the headers, and
/// anything it invents is dropped. It knows nothing about any domain: the schemas are the caller's.
/// </summary>
public sealed class TableShapeClassifier
{
    private readonly IReadOnlyList<TargetSchema> _schemas;
    private readonly TableShapeOptions _options;

    public TableShapeClassifier(IReadOnlyList<TargetSchema> schemas, TableShapeOptions? options = null)
    {
        if (schemas.Count == 0) throw new ArgumentException("At least one target schema is needed.", nameof(schemas));
        _schemas = schemas;
        _options = options ?? new TableShapeOptions();
    }

    public IReadOnlyList<TargetSchema> Schemas => _schemas;

    /// <summary>Deterministic reading only — never calls a model.</summary>
    public ShapeResult Classify(RecordTableReader.Table table)
    {
        // Ranked: headers that say what they are beat a required column guessed from its values; then score.
        var candidates = _schemas.Select(s => Score(s, table))
            .OrderByDescending(c => c.Complete && c.RequiredBoundByHeader).ThenByDescending(c => c.Score).ToList();
        var fits = candidates.Where(c => c.Complete && c.Score >= _options.MinScore).ToList();
        var best = fits.FirstOrDefault();
        var tie = best is not null && fits.Count(c => c.RequiredBoundByHeader == best.RequiredBoundByHeader && c.Score >= best.Score - _options.TieMargin) > 1;
        return new ShapeResult(tie ? null : best, candidates, UsedModel: false,
            best is null ? "No schema fits the headers." : tie ? "Two schemas fit equally well." : null);
    }

    /// <summary>Deterministic first; the residue call only when that is inconclusive (nothing fits, or a tie) and a
    /// call is supplied. <paramref name="scrub"/> runs over every sample cell before it reaches the prompt.</summary>
    public async Task<ShapeResult> ClassifyAsync(
        RecordTableReader.Table table, ShapeResidueCall? residue, Func<string, string>? scrub = null,
        CancellationToken cancellationToken = default)
    {
        var first = Classify(table);
        if (!first.IsUnknown || residue is null || table.Headers.Count == 0) return first;

        var raw = await residue(BuildPrompt(table, scrub), cancellationToken);
        var answer = Parse(raw, table);
        if (answer is null) return first with { UsedModel = true, Note = (first.Note + " The model gave no usable answer.").Trim() };

        var (schema, bindings) = answer.Value;
        // Merge: what the headers already told us for this schema stays; the model fills the rest.
        var deterministic = first.Candidates.First(c => ReferenceEquals(c.Schema, schema));
        var mappings = deterministic.Mappings.ToList();
        var usedHeaders = new HashSet<string>(mappings.Select(m => Norm(m.Header)));
        foreach (var (column, header) in bindings)
        {
            if (mappings.Any(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase)) || usedHeaders.Contains(Norm(header))) continue;
            mappings.Add(new ColumnMapping(schema.Column(column)!.Name, header, _options.ModelConfidence, "model"));
            usedHeaders.Add(Norm(header));
        }
        var merged = new ColumnMap(schema, mappings, ScoreOf(schema, mappings));
        var candidates = first.Candidates.Select(c => ReferenceEquals(c.Schema, schema) ? merged : c).OrderByDescending(c => c.Score).ToList();
        var ok = merged.Complete && merged.Score >= _options.MinScore;
        return new ShapeResult(ok ? merged : null, candidates, UsedModel: true,
            ok ? null : $"The model read it as {schema.Name} but {string.Join(", ", merged.MissingRequired)} could not be bound.");
    }

    // ── deterministic scoring ─────────────────────────────────────────────────────────────────────────────────

    private ColumnMap Score(TargetSchema schema, RecordTableReader.Table table)
    {
        // Every (column, header) pair gets a confidence; then a greedy one-to-one assignment, best pairs first.
        var pairs = new List<(SchemaColumn Column, string Header, double Confidence, string Basis)>();
        foreach (var column in schema.Columns)
            foreach (var header in table.Headers.Where(h => Norm(h).Length > 0).Distinct())
            {
                var (confidence, basis) = HeaderMatch(column, header);
                if (confidence == 0 && column.ValueShape is not null && column.ValueShape(Sample(table, header)))
                    (confidence, basis) = (0.5, "shape");
                else if (confidence > 0 && column.ValueShape is not null && column.ValueShape(Sample(table, header)))
                    confidence = Math.Min(1.0, confidence + 0.1);
                if (confidence > 0) pairs.Add((column, header, confidence, basis));
            }

        var mappings = new List<ColumnMapping>();
        var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedHeaders = new HashSet<string>();
        foreach (var p in pairs.OrderByDescending(p => p.Confidence).ThenBy(p => IndexOf(schema, p.Column)))
        {
            if (usedColumns.Contains(p.Column.Name) || usedHeaders.Contains(Norm(p.Header))) continue;
            mappings.Add(new ColumnMapping(p.Column.Name, p.Header, p.Confidence, p.Basis));
            usedColumns.Add(p.Column.Name);
            usedHeaders.Add(Norm(p.Header));
        }
        return new ColumnMap(schema, mappings, ScoreOf(schema, mappings));
    }

    /// <summary>Required columns weigh double; the score is the bound confidence over the schema's full weight.</summary>
    private static double ScoreOf(TargetSchema schema, IReadOnlyList<ColumnMapping> mappings)
    {
        double total = 0, bound = 0;
        foreach (var c in schema.Columns)
        {
            var w = c.Required ? 2.0 : 1.0;
            total += w;
            var m = mappings.FirstOrDefault(x => string.Equals(x.Column, c.Name, StringComparison.OrdinalIgnoreCase));
            if (m is not null) bound += w * m.Confidence;
        }
        return total == 0 ? 0 : Math.Round(bound / total, 3);
    }

    private static (double, string) HeaderMatch(SchemaColumn column, string header)
    {
        var h = Norm(header);
        if (h == Norm(column.Name)) return (1.0, "exact");
        foreach (var s in column.Synonyms)
        {
            var n = Norm(s);
            if (n.Length == 0) continue;
            if (h == n) return (0.95, "synonym");
        }
        foreach (var s in column.Synonyms.Append(column.Name))
        {
            if (s.Trim().Length < 3) continue;
            if (Regex.IsMatch(header, $@"(^|[^a-z0-9]){Regex.Escape(s.Trim())}($|[^a-z0-9])", RegexOptions.IgnoreCase))
                return (0.7, "contains");
        }
        return (0, "");
    }

    private IReadOnlyList<string> Sample(RecordTableReader.Table table, string header)
    {
        var values = new List<string>();
        foreach (var row in table.Rows)
        {
            var v = table.Cell(row, header).Trim();
            if (v.Length > 0) values.Add(v);
            if (values.Count >= _options.ShapeSample) break;
        }
        return values;
    }

    private static int IndexOf(TargetSchema s, SchemaColumn c) { for (var i = 0; i < s.Columns.Count; i++) if (ReferenceEquals(s.Columns[i], c)) return i; return int.MaxValue; }

    internal static string Norm(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    // ── the residue call ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The prompt the residue call sees: the schemas, the headers, a few scrubbed rows, and the exact JSON asked for.</summary>
    public string BuildPrompt(RecordTableReader.Table table, Func<string, string>? scrub)
    {
        scrub ??= s => s;
        var sb = new StringBuilder();
        sb.AppendLine("A table was uploaded. Decide which ONE of these schemas it is, and which of its headers holds each schema column.");
        sb.AppendLine("Answer ONLY with JSON of the form {\"schema\": \"<schema name>\", \"columns\": {\"<schema column>\": \"<table header>\"}}.");
        sb.AppendLine("Use header names exactly as listed. Leave out any schema column the table does not have. If no schema fits, answer {\"schema\": null}.");
        sb.AppendLine();
        sb.AppendLine("Schemas:");
        foreach (var s in _schemas)
        {
            sb.Append("- ").Append(s.Name);
            if (!string.IsNullOrWhiteSpace(s.Description)) sb.Append(": ").Append(s.Description);
            sb.AppendLine();
            foreach (var c in s.Columns)
                sb.Append("    ").Append(c.Name).Append(c.Required ? " (required)" : "").Append(c.Synonyms.Count > 0 ? $" — also called {string.Join(", ", c.Synonyms.Take(6))}" : "").AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Table headers: " + string.Join(" | ", table.Headers));
        sb.AppendLine("Sample rows:");
        foreach (var row in table.Rows.Take(_options.SampleRows))
            sb.AppendLine("  " + string.Join(" | ", row.Select(v => scrub(v))));
        return sb.ToString();
    }

    /// <summary>Validates the model's answer: the schema must be one of ours, every column one of that schema's, every
    /// header one of the table's. Anything else is dropped; a fabricated schema or an unparseable answer is null.</summary>
    internal (TargetSchema Schema, IReadOnlyList<(string Column, string Header)> Bindings)? Parse(string? raw, RecordTableReader.Table table)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var json = Unfence(raw);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("schema", out var schemaEl) || schemaEl.ValueKind != JsonValueKind.String) return null;
            var schema = _schemas.FirstOrDefault(s => string.Equals(s.Name, schemaEl.GetString(), StringComparison.OrdinalIgnoreCase));
            if (schema is null) return null;

            var headers = table.Headers.ToDictionary(Norm, h => h, StringComparer.Ordinal);
            var bindings = new List<(string, string)>();
            if (root.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Object)
                foreach (var p in cols.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) continue;
                    var column = schema.Column(p.Name);
                    if (column is null) continue;                                       // invented column — dropped
                    if (!headers.TryGetValue(Norm(p.Value.GetString() ?? ""), out var header)) continue;   // invented header — dropped
                    bindings.Add((column.Name, header));
                }
            return (schema, bindings);
        }
        catch (JsonException) { return null; }
    }

    private static string Unfence(string raw)
    {
        var m = Regex.Match(raw, @"\{[\s\S]*\}");
        return m.Success ? m.Value : raw;
    }
}
