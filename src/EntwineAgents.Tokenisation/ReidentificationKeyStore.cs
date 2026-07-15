using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EntwineAgents.Tokenisation;

/// <summary>
/// The reidentification key store (ENT-328 M2). Two jobs:
/// <list type="bullet">
/// <item><b>Register</b> real identities → <b>stable</b> tokens (deterministic per tenant), so the same
/// account gets the same token across runs and partners' graphs converge on shared account nodes.</item>
/// <item><b>Reveal</b> tokens → real identities — the authorised re-hydration path.</item>
/// </list>
/// Stable tokens are derived from a keyed hash of (tenant, type, normalised value); the encrypted real value
/// is stored for reveal. Held apart from the analytics store; real values are encrypted at rest.
/// </summary>
public interface IReidentificationKeyStore
{
    /// <summary>Register real values, returning a real→token map. Idempotent + stable across calls/runs.</summary>
    Task<IReadOnlyDictionary<string, string>> RegisterAsync(
        string tenantId, IReadOnlyList<(string Type, string RealValue)> values, CancellationToken ct = default);

    /// <summary>Authorised reveal: token→real for a tenant. Callers must have re-hydration authorisation.</summary>
    Task<IReadOnlyDictionary<string, string>> RevealAsync(
        string tenantId, IReadOnlyList<string> tokens, CancellationToken ct = default);
}

public sealed class ReidentificationKeyStore(KeyStoreDbContext db, IValueProtector protector) : IReidentificationKeyStore
{
    public async Task<IReadOnlyDictionary<string, string>> RegisterAsync(
        string tenantId, IReadOnlyList<(string Type, string RealValue)> values, CancellationToken ct = default)
    {
        var realToToken = new Dictionary<string, string>();
        var wanted = new Dictionary<string, (string Type, string Real, string Token)>();   // hash -> details

        foreach (var (type, real) in values)
        {
            if (string.IsNullOrWhiteSpace(real)) continue;
            var hash = Hash(tenantId, type, real);
            var token = TokenFor(type, hash);
            realToToken[real] = token;
            wanted.TryAdd(hash, (type, real, token));
        }
        if (wanted.Count == 0) return realToToken;

        var hashes = wanted.Keys.ToList();
        var existing = await db.Entries
            .Where(e => e.TenantId == tenantId && hashes.Contains(e.RealValueHash))
            .Select(e => e.RealValueHash)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        foreach (var (hash, d) in wanted)
        {
            if (existingSet.Contains(hash)) continue;
            db.Entries.Add(new ReidentificationEntry
            {
                TenantId = tenantId,
                TokenType = d.Type,
                Token = d.Token,
                RealValueHash = hash,
                RealValueEncrypted = protector.Protect(d.Real),
            });
        }
        await db.SaveChangesAsync(ct);
        return realToToken;
    }

    public async Task<IReadOnlyDictionary<string, string>> RevealAsync(
        string tenantId, IReadOnlyList<string> tokens, CancellationToken ct = default)
    {
        var set = tokens.Distinct().ToList();
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && set.Contains(e.Token))
            .ToListAsync(ct);
        return entries.ToDictionary(e => e.Token, e => protector.Unprotect(e.RealValueEncrypted));
    }

    // Deterministic per (tenant, type, normalised value) so the token is stable across runs without a lookup.
    private static string Hash(string tenantId, string type, string real)
    {
        var norm = real.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}{type.ToLowerInvariant()}{norm}"));
        return Convert.ToHexString(bytes);
    }

    private static string TokenFor(string type, string hash) =>
        $"{type.ToUpperInvariant()}_{hash[..12].ToLowerInvariant()}";
}
