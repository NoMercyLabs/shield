namespace Shield.Core.Domain;

// Personal-access-token row. The plaintext secret is shown to the operator exactly once at
// creation; this row stores only the SHA256+pepper hash plus a short prefix (for UI display)
// and the scope/filter envelope that constrains what the token can do.
public sealed class ApiToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;

    // Hex-encoded SHA256 of (pepper + plaintext-token). Deterministic so lookup is a single
    // indexed read — the pepper is rotated separately from password hashes.
    public string TokenHash { get; set; } = string.Empty;

    // First 8 chars of the plaintext (after the `shld_` literal) so the UI can render
    // "shld_abcd1234…" without ever holding the secret again.
    public string Prefix { get; set; } = string.Empty;

    // Comma-separated scope catalog: "findings:read,sources:read,sbom:write".
    public string Scopes { get; set; } = string.Empty;

    // Comma-separated source IDs the token is restricted to. Empty = unrestricted within
    // the owning user's existing ACL — never broader than what the user can see.
    public string SourceIdFilter { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }
    public DateTime? RevokedAt { get; set; }
}
