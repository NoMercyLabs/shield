namespace Shield.Core.Domain;

// Persistent per-login row backing the SessionsView UI and the "sign out other sessions"
// flow. The opaque session token written into the shield.session cookie is hashed (SHA256)
// before storage so a DB leak doesn't grant active sessions. RevokedAt != null means the
// SessionTrackingMiddleware will 401 + clear the cookie on the next request that presents it.
public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public string? UserAgent { get; set; }
    public string? RemoteIp { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
