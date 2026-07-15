using EntwineAgents.Tokenisation;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EntwineAgents.TokenisationTest;

/// <summary>
/// The reusable tokenisation primitive in isolation (ENT-321 #2): deterministic cross-run-stable tokens +
/// an AES-GCM encrypted, tenant-scoped key store. No graph, no domain — those round-trips live with their
/// own stores (e.g. the two-store graph test in GraphStoreTest).
/// </summary>
public sealed class KeyStoreTests
{
    private static readonly IValueProtector Protector = new AesValueProtector([.. Enumerable.Repeat((byte)7, 32)]);

    private static (KeyStoreDbContext db, SqliteConnection conn) NewKeys()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new KeyStoreDbContext(new DbContextOptionsBuilder<KeyStoreDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    [Fact]
    public void Aes_protector_round_trips_and_is_non_deterministic()
    {
        var a = Protector.Protect("secret");
        var b = Protector.Protect("secret");
        a.Should().NotBe(b);                       // fresh nonce each time
        Protector.Unprotect(a).Should().Be("secret");
    }

    [Fact]
    public async Task Tokens_are_stable_across_calls_and_runs()
    {
        var (db, conn) = NewKeys();
        var keys = new ReidentificationKeyStore(db, Protector);

        var first = await keys.RegisterAsync("t", [("Account", "ABP Corporation")]);
        var again = await keys.RegisterAsync("t", [("Account", "ABP Corporation")]);   // same identity, later run

        again["ABP Corporation"].Should().Be(first["ABP Corporation"]);   // stable token
        (await db.Entries.CountAsync()).Should().Be(1);                    // idempotent, not duplicated
        conn.Dispose();
    }

    [Fact]
    public async Task Real_values_are_encrypted_at_rest()
    {
        var (db, conn) = NewKeys();
        var keys = new ReidentificationKeyStore(db, Protector);

        await keys.RegisterAsync("t", [("Account", "ABP Corporation")]);

        var entry = await db.Entries.SingleAsync();
        entry.RealValueEncrypted.Should().NotContain("ABP");   // ciphertext, not plaintext
        entry.RealValueHash.Should().NotContain("ABP");
        Protector.Unprotect(entry.RealValueEncrypted).Should().Be("ABP Corporation");
        conn.Dispose();
    }

    [Fact]
    public async Task Reveal_is_scoped_to_the_tenant()
    {
        var (db, conn) = NewKeys();
        var keys = new ReidentificationKeyStore(db, Protector);

        var map = await keys.RegisterAsync("acme", [("Account", "ABP Corporation")]);

        (await keys.RevealAsync("globex", [.. map.Values])).Should().BeEmpty();   // wrong tenant reveals nothing
        (await keys.RevealAsync("acme", [.. map.Values])).Should().ContainValue("ABP Corporation");
        conn.Dispose();
    }
}
