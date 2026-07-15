using Microsoft.EntityFrameworkCore;

namespace EntwineAgents.Tokenisation;

/// <summary>
/// One token → real-identity mapping in the reidentification KEY STORE (ENT-328 M2). This is the crown jewel,
/// held in a store separate from the tokenised analytics store: a breach of the analytics store leaks only
/// tokens; only this store (a different schema / credential) can turn a token back into a name — and its
/// <see cref="RealValueEncrypted"/> is encrypted at rest, so even read access to this table needs the key.
/// </summary>
public sealed class ReidentificationEntry
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "";
    public string TokenType { get; set; } = "";        // Account | Partner | Vendor
    public string Token { get; set; } = "";            // stable, cross-run: TYPE_<hash>
    public string RealValueHash { get; set; } = "";    // deterministic lookup key (real → token) without decrypting
    public string RealValueEncrypted { get; set; } = ""; // ciphertext (token → real reveal)
}

/// <summary>
/// The key store's DbContext — deliberately SEPARATE from <c>GraphStoreDbContext</c> so the two live in
/// different schemas / credentials. Provider-agnostic (Postgres in prod, SQLite in tests).
/// </summary>
public sealed class KeyStoreDbContext(DbContextOptions<KeyStoreDbContext> options) : DbContext(options)
{
    public DbSet<ReidentificationEntry> Entries => Set<ReidentificationEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ReidentificationEntry>(e =>
        {
            e.ToTable("reidentification_keys");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.RealValueHash }).IsUnique();   // one entry per real value (cross-run stable)
            e.HasIndex(x => new { x.TenantId, x.Token });                       // reveal path
        });
    }
}
