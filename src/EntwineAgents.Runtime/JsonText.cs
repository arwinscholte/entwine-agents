namespace EntwineAgents.Runtime;

/// <summary>
/// Output-formatting helpers for agent responses (ENT-321 #4). Models frequently wrap JSON in a Markdown code
/// fence (```json … ``` or ``` … ```); every hand-rolled parser either strips it ad hoc or fails on it (the
/// engagement agents did the latter). <see cref="Unfence"/> normalises the raw text once so parsers can just
/// <c>JsonDocument.Parse</c> the result.
/// </summary>
public static class JsonText
{
    /// <summary>Strip a leading/trailing Markdown code fence (any language tag) if present; otherwise return trimmed input.</summary>
    public static string Unfence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        var s = raw.Trim();
        if (!s.StartsWith("```")) return s;

        var firstNewline = s.IndexOf('\n');
        s = firstNewline >= 0 ? s[(firstNewline + 1)..] : s[3..];   // drop the opening ``` / ```json line
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}
