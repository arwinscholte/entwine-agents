using System.Text.RegularExpressions;

namespace EntwineAgents.Intake;

/// <summary>
/// Session-local, in-memory pseudonymisation (ENT-326, hoisted in ENT-321 #3a). Real identities are replaced
/// with typed, stable tokens (<c>ACCOUNT_01</c>, <c>PARTNER_01</c>, <c>VENDOR_01</c>) before anything is sent
/// to an LLM, and re-hydrated back for the human-facing output. The map is never sent to the model and never
/// persisted — it lives only for the session and dies with it. Tokens are <b>typed, not blind-hashed</b>, so
/// the role signal (customer vs partner vs vendor) survives; only the identity is hidden, never the descriptor.
///
/// This is the domain-neutral core. Domain callers subclass it to add typed convenience overloads (e.g. the
/// concierge's <c>ReidentificationMap</c> adds <c>Anonymize(RecordInput)</c> / <c>Hydrate(ClassifiedRecord)</c>).
/// For tokenisation AT REST across runs, use the separate encrypted key store in EntwineAgents.Tokenisation.
/// </summary>
public class PseudonymMap
{
    private readonly Dictionary<string, string> _accountToken = new(StringComparer.OrdinalIgnoreCase); // real → token
    private readonly Dictionary<string, string> _partnerToken = new(StringComparer.OrdinalIgnoreCase); // real → token
    private readonly Dictionary<string, string> _vendorToken = new(StringComparer.OrdinalIgnoreCase);  // real → token
    private readonly Dictionary<string, string> _tokenToReal = new(StringComparer.Ordinal);            // token → real
    private int _accountSeq;
    private int _partnerSeq;
    private int _vendorSeq;

    /// <summary>Distinct identities held (for diagnostics/tests). The map itself is never exposed.</summary>
    public int Count => _tokenToReal.Count;

    /// <summary>Stable token for an account name — same real name always yields the same token.</summary>
    public string AnonymizeAccount(string? realName) => Anonymize(realName, _accountToken, "ACCOUNT", ref _accountSeq);

    /// <summary>Stable typed token for a partner name (for the tokenised subgraph, ENT-331).</summary>
    public string AnonymizePartner(string? realName) => Anonymize(realName, _partnerToken, "PARTNER", ref _partnerSeq);

    /// <summary>Stable typed token for a vendor / product name (for the tokenised subgraph, ENT-331).</summary>
    public string AnonymizeVendor(string? realName) => Anonymize(realName, _vendorToken, "VENDOR", ref _vendorSeq);

    private string Anonymize(string? realName, Dictionary<string, string> map, string prefix, ref int seq)
    {
        if (string.IsNullOrWhiteSpace(realName)) return string.Empty;
        var name = realName.Trim();
        if (map.TryGetValue(name, out var existing)) return existing;
        var token = $"{prefix}_{++seq:00}";
        map[name] = token;
        _tokenToReal[token] = name;
        return token;
    }

    /// <summary>Replace any already-registered real identity embedded in free text with its token.</summary>
    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var map in new[] { _accountToken, _partnerToken, _vendorToken })
            foreach (var (real, token) in map)
                if (real.Length >= 4)   // avoid scrubbing very short names that could collide with common words
                    text = Regex.Replace(text, Regex.Escape(real), token, RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>Turn tokens back into real identities for display.</summary>
    public string Hydrate(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (token, real) in _tokenToReal)
            text = text.Replace(token, real);
        return text;
    }
}
