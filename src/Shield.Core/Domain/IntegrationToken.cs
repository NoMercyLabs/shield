namespace Shield.Core.Domain;

// One row per (Provider, Subject). AccessToken/RefreshToken are encrypted at rest
// via IDataProtector (purpose "shield.oauth"); the OAuthTokenStore is the only reader.
// Subject is the provider's stable user id (sub/id) — connect-flow rows leave it empty
// so the legacy single-row-per-provider semantics still resolve via the empty-subject row.
// LinkedUserId is set by the signin flow so we can find the local user on subsequent logins.
public sealed class IntegrationToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OAuthProvider Provider { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string AccessTokenEncrypted { get; set; } = string.Empty;
    public string? RefreshTokenEncrypted { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public string AccountLogin { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string? Extra { get; set; }
    public Guid? LinkedUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
