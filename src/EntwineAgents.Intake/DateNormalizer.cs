using System.Globalization;

namespace EntwineAgents.Intake;

/// <summary>
/// Defensive date parsing (ENT-001 ruleset §6). Real DCPPA exports carry Excel serials, day/month swaps
/// that overflow (e.g. "2017-27-03"), sentinel years ("0001-01-01", "3000-01-01"), and blank/ongoing end
/// dates. Malformed values become <c>null</c> + a <c>date_invalid</c> flag — the engagement is kept, never
/// dropped. A blank / "-" / "On-going" / "TBD" end means the engagement is still active.
/// </summary>
public static class DateNormalizer
{
    private static readonly string[] OngoingMarkers = ["-", "on-going", "ongoing", "tbd", "current", "present"];

    public readonly record struct Result(DateOnly? Start, bool IsActive, string? Flag);

    public static Result Normalize(string? start, string? end)
    {
        var isActive = IsOngoing(end);
        var (date, valid) = ParseStart(start);
        return new Result(date, isActive, valid ? null : "date_invalid");
    }

    private static bool IsOngoing(string? end)
    {
        if (string.IsNullOrWhiteSpace(end))
            return true;
        var e = end.Trim().ToLowerInvariant();
        return OngoingMarkers.Contains(e);
    }

    private static (DateOnly? date, bool valid) ParseStart(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return (null, false);

        // Take the date part before any time component ("2019-11-01 00:00:00" -> "2019-11-01").
        var token = start.Trim().Split(' ', 'T')[0];

        // Excel serial number (a bare integer) -> days since 1899-12-30.
        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial)
            && serial is > 0 and < 60000)
        {
            var d = new DateOnly(1899, 12, 30).AddDays((int)serial);
            return WithinRange(d) ? (d, true) : (null, false);
        }

        string[] formats = ["yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy", "dd/MM/yyyy"];
        if (DateOnly.TryParseExact(token, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return WithinRange(parsed) ? (parsed, true) : (null, false);

        // Overflowing day/month swaps (e.g. "2017-27-03") fail TryParseExact -> flagged, kept null.
        return (null, false);
    }

    // Reject sentinel years that are clearly not real engagement dates.
    private static bool WithinRange(DateOnly d) => d.Year is >= 1990 and <= 2100;
}
