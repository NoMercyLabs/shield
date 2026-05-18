using Shield.Core.Domain;

namespace Shield.Api.Services.Auth;

// Encapsulates the opaque session-token lifecycle so login/logout/middleware don't have to
// share string format / SHA256 prefix conventions.
public interface ISessionTracker
{
    // Mints a new session row + returns the opaque token (cookie value).
    Task<(UserSession Session, string OpaqueToken)> CreateAsync(
        Guid userId,
        string? userAgent,
        string? remoteIp,
        CancellationToken ct = default
    );

    Task<UserSession?> FindByOpaqueTokenAsync(string opaqueToken, CancellationToken ct = default);
    Task TouchAsync(UserSession session, CancellationToken ct = default);
    Task RevokeAsync(Guid sessionId, CancellationToken ct = default);
    Task<int> RevokeOthersAsync(Guid userId, Guid keepSessionId, CancellationToken ct = default);

    // Hard-revoke EVERY session for the user (including the current one). Returns the count
    // of rows transitioned. Used by the "sign out everywhere" panic button.
    Task<int> RevokeAllAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserSession>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSession>> ListAllAsync(CancellationToken ct = default);

    // Returns true when a same-userAgent session existed for this user within the lookback
    // window — used to suppress notification spam on tab-restore / browser-relaunch loops.
    Task<bool> HasRecentSameDeviceSessionAsync(
        Guid userId,
        Guid currentSessionId,
        string? userAgent,
        TimeSpan lookback,
        CancellationToken ct = default
    );

    // Stable across processes — used by login/middleware to derive the lookup key
    // without leaking the raw cookie value into the DB.
    string Hash(string opaqueToken);
}
