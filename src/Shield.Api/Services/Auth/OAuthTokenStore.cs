using Microsoft.AspNetCore.DataProtection;

namespace Shield.Api.Services.Auth;

// Persistence layer for connected OAuth integrations. Tokens are encrypted at rest
// via IDataProtector(purpose: "shield.oauth"). Reads are cached in-memory; writes go
// straight to the DB and refresh the cache. Singleton — uses IServiceScopeFactory
// to open a scoped DbContext per operation.
public interface IOAuthTokenStore
{
    // Connect-flow row: subject is empty so feed/source workers find the one-per-provider token.
    Task<OAuthTokenSnapshot?> GetAsync(OAuthProvider provider, CancellationToken ct = default);
    Task SaveAsync(OAuthTokenSnapshot snapshot, CancellationToken ct = default);

    // Same upsert as SaveAsync but also stamps the local Shield user that wired up the
    // integration. Used by flows where the actor's identity is known at write time
    // (device-flow connect, where the request itself is authenticated).
    Task SaveAsync(OAuthTokenSnapshot snapshot, Guid linkedUserId, CancellationToken ct = default);
    Task DisconnectAsync(OAuthProvider provider, CancellationToken ct = default);

    // Signin-flow rows: keyed by subject (provider user id) so the callback can find the linked user.
    Task<IntegrationTokenLookup?> FindBySubjectAsync(
        OAuthProvider provider,
        string subject,
        CancellationToken ct = default
    );
    Task<Guid> SaveSigninAsync(
        OAuthTokenSnapshot snapshot,
        string subject,
        Guid linkedUserId,
        CancellationToken ct = default
    );

    // Returns the per-user signin token snapshot (decrypted) so callers can talk to the
    // provider AS the user. Falls back to null when there's no signin row OR when the
    // encrypted blob can't be decrypted (key rotated). Distinct from GetAsync, which only
    // ever reads the empty-subject connect-flow row.
    Task<OAuthTokenSnapshot?> GetSigninAsync(
        OAuthProvider provider,
        string subject,
        CancellationToken ct = default
    );
}

public sealed record OAuthTokenSnapshot(
    OAuthProvider Provider,
    string AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string Scopes,
    string AccountLogin,
    string? AccountId,
    string? Extra
);

public sealed record IntegrationTokenLookup(
    Guid Id,
    Guid? LinkedUserId,
    string AccountLogin,
    string? AccountEmail
);

public sealed class OAuthTokenStore : IOAuthTokenStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public OAuthTokenStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection)
    {
        _scopeFactory = scopeFactory;
        _protector = protection.CreateProtector("shield.oauth");
    }

    public async Task<OAuthTokenSnapshot?> GetAsync(
        OAuthProvider provider,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        // Connect-flow row is keyed by an empty Subject; signin rows live alongside with a real subject.
        IntegrationToken? row = await db
            .IntegrationTokens.AsNoTracking()
            .FirstOrDefaultAsync(token => token.Provider == provider && token.Subject == "", ct);
        if (row is null)
            return null;

        string accessToken;
        string? refreshToken;
        try
        {
            accessToken = _protector.Unprotect(row.AccessTokenEncrypted);
            refreshToken = string.IsNullOrEmpty(row.RefreshTokenEncrypted)
                ? null
                : _protector.Unprotect(row.RefreshTokenEncrypted);
        }
        catch
        {
            // Unreadable row (key rotated / corrupt) — treat as disconnected.
            return null;
        }

        return new(
            row.Provider,
            accessToken,
            refreshToken,
            row.ExpiresAt,
            row.Scopes,
            row.AccountLogin,
            row.AccountId,
            row.Extra
        );
    }

    public Task SaveAsync(OAuthTokenSnapshot snapshot, CancellationToken ct = default) =>
        SaveConnectInternalAsync(snapshot, linkedUserId: null, ct);

    public Task SaveAsync(
        OAuthTokenSnapshot snapshot,
        Guid linkedUserId,
        CancellationToken ct = default
    ) => SaveConnectInternalAsync(snapshot, linkedUserId, ct);

    private async Task SaveConnectInternalAsync(
        OAuthTokenSnapshot snapshot,
        Guid? linkedUserId,
        CancellationToken ct
    )
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            IntegrationToken? existing = await db.IntegrationTokens.FirstOrDefaultAsync(
                token => token.Provider == snapshot.Provider && token.Subject == "",
                ct
            );

            DateTime now = DateTime.UtcNow;
            string encryptedAccess = _protector.Protect(snapshot.AccessToken);
            string? encryptedRefresh = string.IsNullOrEmpty(snapshot.RefreshToken)
                ? null
                : _protector.Protect(snapshot.RefreshToken);

            if (existing is null)
            {
                db.IntegrationTokens.Add(
                    new()
                    {
                        Provider = snapshot.Provider,
                        Subject = string.Empty,
                        AccessTokenEncrypted = encryptedAccess,
                        RefreshTokenEncrypted = encryptedRefresh,
                        ExpiresAt = snapshot.ExpiresAt,
                        Scopes = snapshot.Scopes,
                        AccountLogin = snapshot.AccountLogin,
                        AccountId = snapshot.AccountId,
                        Extra = snapshot.Extra,
                        LinkedUserId = linkedUserId,
                        CreatedAt = now,
                        UpdatedAt = now,
                    }
                );
            }
            else
            {
                existing.AccessTokenEncrypted = encryptedAccess;
                // Don't clobber the refresh token if the provider didn't send a new one (e.g. GitHub on refresh).
                if (encryptedRefresh is not null)
                    existing.RefreshTokenEncrypted = encryptedRefresh;
                existing.ExpiresAt = snapshot.ExpiresAt;
                existing.Scopes = snapshot.Scopes;
                existing.AccountLogin = snapshot.AccountLogin;
                existing.AccountId = snapshot.AccountId;
                existing.Extra = snapshot.Extra;
                if (linkedUserId is not null)
                    existing.LinkedUserId = linkedUserId;
                existing.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync(OAuthProvider provider, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            IntegrationToken? existing = await db.IntegrationTokens.FirstOrDefaultAsync(
                token => token.Provider == provider && token.Subject == "",
                ct
            );
            if (existing is null)
                return;
            db.IntegrationTokens.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IntegrationTokenLookup?> FindBySubjectAsync(
        OAuthProvider provider,
        string subject,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(subject))
            return null;
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IntegrationToken? row = await db
            .IntegrationTokens.AsNoTracking()
            .FirstOrDefaultAsync(
                token => token.Provider == provider && token.Subject == subject,
                ct
            );
        return row is null
            ? null
            : new IntegrationTokenLookup(row.Id, row.LinkedUserId, row.AccountLogin, row.AccountId);
    }

    public async Task<OAuthTokenSnapshot?> GetSigninAsync(
        OAuthProvider provider,
        string subject,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(subject))
            return null;
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IntegrationToken? row = await db
            .IntegrationTokens.AsNoTracking()
            .FirstOrDefaultAsync(
                token => token.Provider == provider && token.Subject == subject,
                ct
            );
        if (row is null)
            return null;

        string accessToken;
        string? refreshToken;
        try
        {
            accessToken = _protector.Unprotect(row.AccessTokenEncrypted);
            refreshToken = string.IsNullOrEmpty(row.RefreshTokenEncrypted)
                ? null
                : _protector.Unprotect(row.RefreshTokenEncrypted);
        }
        catch
        {
            return null;
        }

        return new(
            row.Provider,
            accessToken,
            refreshToken,
            row.ExpiresAt,
            row.Scopes,
            row.AccountLogin,
            row.AccountId,
            row.Extra
        );
    }

    public async Task<Guid> SaveSigninAsync(
        OAuthTokenSnapshot snapshot,
        string subject,
        Guid linkedUserId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject is required for signin rows", nameof(subject));
        await _writeLock.WaitAsync(ct);
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            IntegrationToken? existing = await db.IntegrationTokens.FirstOrDefaultAsync(
                token => token.Provider == snapshot.Provider && token.Subject == subject,
                ct
            );

            DateTime now = DateTime.UtcNow;
            string encryptedAccess = _protector.Protect(snapshot.AccessToken);
            string? encryptedRefresh = string.IsNullOrEmpty(snapshot.RefreshToken)
                ? null
                : _protector.Protect(snapshot.RefreshToken);

            if (existing is null)
            {
                IntegrationToken row = new()
                {
                    Provider = snapshot.Provider,
                    Subject = subject,
                    AccessTokenEncrypted = encryptedAccess,
                    RefreshTokenEncrypted = encryptedRefresh,
                    ExpiresAt = snapshot.ExpiresAt,
                    Scopes = snapshot.Scopes,
                    AccountLogin = snapshot.AccountLogin,
                    AccountId = snapshot.AccountId,
                    Extra = snapshot.Extra,
                    LinkedUserId = linkedUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.IntegrationTokens.Add(row);
                await db.SaveChangesAsync(ct);
                return row.Id;
            }

            existing.AccessTokenEncrypted = encryptedAccess;
            if (encryptedRefresh is not null)
                existing.RefreshTokenEncrypted = encryptedRefresh;
            existing.ExpiresAt = snapshot.ExpiresAt;
            existing.Scopes = snapshot.Scopes;
            existing.AccountLogin = snapshot.AccountLogin;
            existing.AccountId = snapshot.AccountId;
            existing.Extra = snapshot.Extra;
            existing.LinkedUserId = linkedUserId;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return existing.Id;
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
