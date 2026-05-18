using Shield.Core.Domain;

namespace Shield.Api.Services.Auth;

// Wraps ApiToken CRUD + plaintext lookup. The plaintext token is only returned once,
// at creation time; every subsequent lookup is hash-only.
public interface IApiTokenStore
{
    // Generates a fresh `shld_<32-base32>` token, persists the hash, returns the row + plaintext.
    Task<(ApiToken Token, string PlaintextSecret)> CreateAsync(
        Guid userId,
        string name,
        IEnumerable<string> scopes,
        DateTime? expiresAt,
        IEnumerable<int> sourceIdFilter,
        CancellationToken ct = default
    );

    // Returns the token row for a presented `shld_*` secret, or null if revoked / expired /
    // unknown. Side-effect: refreshes LastUsedAt + LastUsedIp at most once per minute per token.
    Task<ApiToken?> FindByPlaintextAsync(
        string plaintext,
        string? remoteIp,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<ApiToken>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ApiToken>> ListAllAsync(CancellationToken ct = default);

    Task<bool> RevokeAsync(
        Guid id,
        Guid? requestingUserId,
        bool isAdmin,
        CancellationToken ct = default
    );
}
