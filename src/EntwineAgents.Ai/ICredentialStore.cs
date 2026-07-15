namespace EntwineAgents.Ai;

/// <summary>
/// Per-client provider credential store. The clear API key never leaves this seam except
/// through <see cref="GetSecretAsync"/>, which the egress endpoint exposes only to the
/// authenticated in-app service principal. Admin/browser surfaces use <see cref="ListMaskedAsync"/>.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Create or replace a client's credential for a provider. Encrypts at rest; returns the masked view.</summary>
    Task<ProviderCredentialMaskedDto> UpsertAsync(int clientId, string providerKey, ProviderCredentialUpsert input, CancellationToken cancellationToken = default);

    /// <summary>List a client's credentials with keys masked — safe for admin/browser.</summary>
    Task<IReadOnlyList<ProviderCredentialMaskedDto>> ListMaskedAsync(int clientId, CancellationToken cancellationToken = default);

    /// <summary>Resolve a client's decrypted credential for one provider. Null if absent/inactive/undecryptable.</summary>
    Task<ProviderCredentialDto?> GetSecretAsync(int clientId, string providerKey, CancellationToken cancellationToken = default);
}

/// <summary>Upsert input — the clear API key plus optional endpoint/model overrides.</summary>
public sealed class ProviderCredentialUpsert
{
    public string ApiKey { get; set; } = string.Empty;   // required (non-nullable → [ApiController] enforces)
    public string? BaseUrl { get; set; }
    public string? ModelId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Masked projection — never carries the clear key.</summary>
public sealed record ProviderCredentialMaskedDto(
    string ProviderKey,
    string MaskedApiKey,
    string? BaseUrl,
    string? ModelId,
    bool IsActive,
    DateTime UpdatedAt);

/// <summary>Decrypted projection — released only to the in-app service principal via the egress endpoint.</summary>
public sealed record ProviderCredentialDto(
    string ProviderKey,
    string ApiKey,
    string? BaseUrl,
    string? ModelId,
    bool IsActive,
    DateTime UpdatedAt);
