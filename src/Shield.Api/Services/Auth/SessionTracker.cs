using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services.Auth;

public sealed class SessionTracker : ISessionTracker
{
    // 32 bytes → 256 bits of entropy in the cookie. Encoded as URL-safe base64 so the
    // cookie value can travel through Set-Cookie without re-encoding.
    private const int OpaqueTokenByteLength = 32;

    // Write-coalesce LastActiveAt — saves a DB roundtrip on every request from an active SPA.
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly ShieldDbContext _db;

    public SessionTracker(ShieldDbContext db)
    {
        _db = db;
    }

    public async Task<(UserSession Session, string OpaqueToken)> CreateAsync(
        Guid userId,
        string? userAgent,
        string? remoteIp,
        CancellationToken ct = default
    )
    {
        string opaqueToken = GenerateOpaqueToken();
        DateTime now = DateTime.UtcNow;
        UserSession session = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(opaqueToken),
            UserAgent = Truncate(userAgent, 512),
            RemoteIp = Truncate(remoteIp, 64),
            CreatedAt = now,
            LastActiveAt = now,
        };
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return (session, opaqueToken);
    }

    public async Task<UserSession?> FindByOpaqueTokenAsync(
        string opaqueToken,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(opaqueToken))
            return null;
        string hash = Hash(opaqueToken);
        return await _db.UserSessions.FirstOrDefaultAsync(session => session.TokenHash == hash, ct);
    }

    public async Task TouchAsync(UserSession session, CancellationToken ct = default)
    {
        DateTime now = DateTime.UtcNow;
        if (now - session.LastActiveAt < TouchInterval)
            return;
        session.LastActiveAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        UserSession? session = await _db.UserSessions.FirstOrDefaultAsync(
            row => row.Id == sessionId,
            ct
        );
        if (session is null || session.RevokedAt is not null)
            return;
        session.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> RevokeOthersAsync(
        Guid userId,
        Guid keepSessionId,
        CancellationToken ct = default
    )
    {
        List<UserSession> others = await _db
            .UserSessions.Where(session =>
                session.UserId == userId && session.Id != keepSessionId && session.RevokedAt == null
            )
            .ToListAsync(ct);
        DateTime now = DateTime.UtcNow;
        foreach (UserSession session in others)
            session.RevokedAt = now;
        await _db.SaveChangesAsync(ct);
        return others.Count;
    }

    public async Task<int> RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        List<UserSession> rows = await _db
            .UserSessions.Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(ct);
        DateTime now = DateTime.UtcNow;
        foreach (UserSession session in rows)
            session.RevokedAt = now;
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<bool> HasRecentSameDeviceSessionAsync(
        Guid userId,
        Guid currentSessionId,
        string? userAgent,
        TimeSpan lookback,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;
        DateTime threshold = DateTime.UtcNow - lookback;
        return await _db.UserSessions.AnyAsync(
            session =>
                session.UserId == userId
                && session.Id != currentSessionId
                && session.UserAgent == userAgent
                && session.CreatedAt >= threshold,
            ct
        );
    }

    public async Task<IReadOnlyList<UserSession>> ListAsync(
        Guid userId,
        CancellationToken ct = default
    ) =>
        await _db
            .UserSessions.Where(session => session.UserId == userId && session.RevokedAt == null)
            .OrderByDescending(session => session.LastActiveAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserSession>> ListAllAsync(CancellationToken ct = default) =>
        await _db
            .UserSessions.Where(session => session.RevokedAt == null)
            .OrderByDescending(session => session.LastActiveAt)
            .ToListAsync(ct);

    public string Hash(string opaqueToken)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateOpaqueToken()
    {
        Span<byte> bytes = stackalloc byte[OpaqueTokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= max ? value
        : value[..max];
}
