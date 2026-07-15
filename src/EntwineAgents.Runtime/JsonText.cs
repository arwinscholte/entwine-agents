namespace EntwineAgents.Runtime;

/// <summary>
/// Output-formatting helpers for agent responses (ENT-321 #4). Models frequently wrap JSON in a Markdown code
/// fence (```json … ``` or ``` … ```); every hand-rolled parser either strips it ad hoc or fails on it (the
/// engagement agents did the latter). <see cref="Unfence"/> normalises the raw text once so parsers can just
/// <c>JsonDocument.Parse</c> the result.
/// </summary>
public static class JsonText
{
    /// <summary>
    /// Return the content of the first Markdown code fence (any language tag) if the input contains one —
    /// including when the model prefixes prose ("Here is the JSON: ```json …") — otherwise the trimmed input.
    /// </summary>
    public static string Unfence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        var s = raw.Trim();

        var open = s.IndexOf("```", StringComparison.Ordinal);
        if (open < 0) return s;   // no fence anywhere — pass through

        // Content starts after the opening fence's line (skips a language tag like ```json); for a
        // single-line fence there is no newline, so it starts right after the backticks.
        var newline = s.IndexOf('\n', open);
        var contentStart = newline < 0 ? open + 3 : newline + 1;

        var close = s.IndexOf("```", contentStart, StringComparison.Ordinal);
        var content = close < 0 ? s[contentStart..] : s[contentStart..close];
        return content.Trim();
    }
}
