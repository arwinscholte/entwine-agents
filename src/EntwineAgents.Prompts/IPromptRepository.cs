
namespace EntwineAgents.Prompts;

/// <summary>
/// Persistence port for prompt templates (ENT-321 #5b). Everything <see cref="PromptService"/> needs from the
/// store, behind an interface — so the service depends on this, not on the tenant <c>ApplicationDbContext</c>.
/// The EF implementation (<c>EfPromptRepository</c>) stays in the app; the versioning transaction semantics
/// (archive-active + insert-next-version, rollback) live in the adapter, since they are a persistence concern.
/// The service keeps the caching, client→global fallback policy, and the auto-analyse orchestration.
/// </summary>
public interface IPromptRepository
{
    /// <summary>Highest active version's text for an exact (key, clientId) — clientId null means the global default.</summary>
    Task<string?> GetActiveTextAsync(string promptKey, int? clientId, CancellationToken ct = default);

    /// <summary>Highest active version's model override for an exact (key, clientId).</summary>
    Task<string?> GetActiveModelIdAsync(string promptKey, int? clientId, CancellationToken ct = default);

    /// <summary>The active version of every prompt visible to the client (global + client-specific).</summary>
    Task<List<PromptTemplate>> GetAllActiveAsync(int? clientId, CancellationToken ct = default);

    /// <summary>Full version history for a (key, clientId), newest first.</summary>
    Task<List<PromptTemplate>> GetHistoryAsync(string promptKey, int? clientId, CancellationToken ct = default);

    /// <summary>Archive the current active version and insert <paramref name="template"/> as the next active version.</summary>
    Task<PromptUpsertResult> UpsertVersionAsync(PromptTemplate template, CancellationToken ct = default);

    /// <summary>Deactivate all client-specific versions of a key (reset to the global default; history preserved).</summary>
    Task DeactivateClientVersionsAsync(string promptKey, int clientId, CancellationToken ct = default);

    /// <summary>Re-activate a prior version by copying it into a new active version; null if that version is absent.</summary>
    Task<PromptTemplate?> RollbackAsync(string promptKey, int? clientId, int version, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IPromptRepository.UpsertVersionAsync"/>: the new active row and the version it
/// superseded (null on first insert) — the service uses <see cref="PreviousVersion"/> to trigger auto-analysis.</summary>
public sealed record PromptUpsertResult(PromptTemplate NewRow, int? PreviousVersion);
