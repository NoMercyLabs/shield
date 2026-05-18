using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Shield.Api.Services.Auth;

public sealed class ApiTokenStore : IApiTokenStore
{
    public const string TokenPrefix = "shld_";
    public const int SecretEntropyBytes = 20; // 20 bytes → 32 base32 chars (160 bits).
    public const int PrefixDisplayLength = 8;

    // Base32 RFC4648 alphabet without padding. Avoids `=` in URLs and `+`/`/` from base64.
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static readonly TimeSpan LastUsedCoalesceWindow = TimeSpan.FromSeconds(60);

    // Process-wide gate so concurrent requests bearing the same token write LastUsedAt at most
    // once per minute. Singleton-scoped state would be ideal but the store is Scoped (it owns
    // a DbContext), so a static keeps the gate shared without elevating the service lifetime.
    private static readonly ConcurrentDictionary<Guid, DateTime> s_lastPersistedAt = new();

    private readonly ShieldDbContext _db;
    private readonly string _pepper;

    public ApiTokenStore(ShieldDbContext db, IConfiguration configuration, IHostEnvironment env)
    {
        _db = db;
        string? pepper = configuration["Shield:Auth:ApiTokenPepper"];
        if (string.IsNullOrEmpty(pepper))
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
                throw new InvalidOperationException(
                    "Shield:Auth:ApiTokenPepper is required in non-Development environments — "
                        + "without it the on-disk hashes can be brute-forced from a DB leak alone."
                );
            // Dev/Testing fallback — deterministic so SQLite restarts don't invalidate dev tokens.
            pepper = "dev-only-api-token-pepper-do-not-use-in-prod";
        }
        _pepper = pepper;
    }

    public async Task<(ApiToken Token, string PlaintextSecret)> CreateAsync(
        Guid userId,
        string name,
        IEnumerable<string> scopes,
        DateTime? expiresAt,
        IEnumerable<int> sourceIdFilter,
        CancellationToken ct = default
    )
    {
        string secret = GenerateSecret();
        string plaintextWithPrefix = TokenPrefix + secret;
        string hash = HashSecret(plaintextWithPrefix);

        // Normalise scopes: trim, drop empties, distinct, lowercase.
        string scopeList = string.Join(
            ',',
            scopes
                .Select(scope => scope?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(scope => scope.Length > 0)
                .Distinct()
        );

        string sourceFilter = string.Join(',', sourceIdFilter.Distinct().OrderBy(id => id));

        ApiToken token = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            TokenHash = hash,
            Prefix = secret[..PrefixDisplayLength],
            Scopes = scopeList,
            SourceIdFilter = sourceFilter,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        _db.ApiTokens.Add(token);
        await _db.SaveChangesAsync(ct);
        return (token, plaintextWithPrefix);
    }

    public async Task<ApiToken?> FindByPlaintextAsync(
        string plaintext,
        string? remoteIp,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrEmpty(plaintext)
            || !plaintext.StartsWith(TokenPrefix, StringComparison.Ordinal)
        )
            return null;

        string hash = HashSecret(plaintext);
        ApiToken? token = await _db.ApiTokens.FirstOrDefaultAsync(row => row.TokenHash == hash, ct);
        if (token is null)
            return null;
        if (token.RevokedAt is not null)
            return null;
        if (token.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return null;

        // Coalesce LastUsedAt writes so a high-volume CI bearer doesn't hammer SQLite.
        DateTime now = DateTime.UtcNow;
        DateTime persistedAt = s_lastPersistedAt.GetOrAdd(token.Id, DateTime.MinValue);
        if (now - persistedAt >= LastUsedCoalesceWindow)
        {
            s_lastPersistedAt[token.Id] = now;
            token.LastUsedAt = now;
            token.LastUsedIp = remoteIp;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request beat us to the write — the contract is "approximately every
                // 60 seconds", so swallow the conflict instead of failing the auth.
            }
        }

        return token;
    }

    public async Task<IReadOnlyList<ApiToken>> ListForUserAsync(
        Guid userId,
        CancellationToken ct = default
    )
    {
        return await _db
            .ApiTokens.Where(token => token.UserId == userId)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiToken>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.ApiTokens.OrderByDescending(token => token.CreatedAt).ToListAsync(ct);
    }

    public async Task<bool> RevokeAsync(
        Guid id,
        Guid? requestingUserId,
        bool isAdmin,
        CancellationToken ct = default
    )
    {
        ApiToken? token = await _db.ApiTokens.FirstOrDefaultAsync(row => row.Id == id, ct);
        if (token is null)
            return false;
        if (!isAdmin && token.UserId != requestingUserId)
            return false;
        if (token.RevokedAt is not null)
            return true; // idempotent

        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        s_lastPersistedAt.TryRemove(token.Id, out _);
        return true;
    }

    private static string GenerateSecret()
    {
        Span<byte> bytes = stackalloc byte[SecretEntropyBytes];
        RandomNumberGenerator.Fill(bytes);
        return EncodeBase32(bytes);
    }

    private string HashSecret(string plaintext)
    {
        byte[] payload = Encoding.UTF8.GetBytes(_pepper + plaintext);
        byte[] hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }

    // Tight inline RFC4648 base32 encoder — avoids pulling a NuGet for 20 bytes of input.
    private static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        StringBuilder builder = new(bytes.Length * 8 / 5 + 1);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                int index = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                builder.Append(Base32Alphabet[index]);
            }
        }
        if (bitsLeft > 0)
        {
            int index = (buffer << (5 - bitsLeft)) & 0x1F;
            builder.Append(Base32Alphabet[index]);
        }
        return builder.ToString();
    }

    // Test-only helper for the coalesce gate — keeps the static state from polluting tests
    // that exercise back-to-back FindByPlaintextAsync calls expecting an immediate write.
    public static void ResetCoalesceGate()
    {
        s_lastPersistedAt.Clear();
    }
}
