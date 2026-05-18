namespace Shield.Api.Services.AppSettings;

// Runtime-mutable settings cache. Singleton; Current is refreshed on first read and
// after every UpdateAsync. Changed fires synchronously so auth/middleware see the new
// snapshot before the response returns.
public interface IAppSettingsService
{
    AppSettingsSnapshot Current { get; }
    Task<AppSettingsSnapshot> GetAsync(CancellationToken ct = default);
    Task<AppSettingsSnapshot> UpdateAsync(
        AppSettingsPatch patch,
        Guid? updatedBy,
        CancellationToken ct = default
    );
    Task<AppSettingsSnapshot> ReloadAsync(CancellationToken ct = default);

    // Free-form key reader for settings not yet promoted to the typed snapshot.
    // Returns fallback when the key is missing or the stored value can't parse as bool.
    Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken ct = default);
    Task SetBoolAsync(string key, bool value, Guid? updatedBy, CancellationToken ct = default);

    // Free-form string read/write — the value is encrypted at rest via DataProtection, same
    // envelope as every other AppSetting row. Returns null when the key is missing or the
    // stored ciphertext can't decrypt (rotated key / corrupt row).
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);
    Task SetStringAsync(string key, string value, Guid? updatedBy, CancellationToken ct = default);
    event Action<AppSettingsSnapshot>? Changed;
}

public sealed record AppSettingsSnapshot(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecret,
    Severity AlertSeverityFloor,
    int RetentionDays,
    bool RegistrationOpen,
    OAuthClientSettings GithubOAuth,
    OAuthClientSettings SlackOAuth,
    OAuthClientSettings GoogleOAuth,
    OAuthClientSettings GitlabOAuth,
    OAuthClientSettings BitbucketOAuth,
    OAuthClientSettings ForgejoOAuth,
    OAuthClientSettings GiteaOAuth,
    OAuthClientSettings CodebergOAuth,
    string? OAuthRedirectBase,
    IReadOnlyList<string> DetectedRemoteHosts
);

// ClientId/Scopes are plain strings; ClientSecret is held decrypted in-memory only
// (loaded via the same DataProtector that encrypts AppSetting rows). Host is set for
// self-hosted Forgejo + Gitea instances; null for SaaS providers.
public sealed record OAuthClientSettings(
    string? ClientId,
    string? ClientSecret,
    string? Scopes,
    string? Host = null
);

// PreserveOidcClientSecret=true means "leave whatever is stored alone"; otherwise the
// provided ClientSecret value (including null/empty) overwrites the row.
public sealed record AppSettingsPatch(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecret,
    bool PreserveOidcClientSecret,
    Severity AlertSeverityFloor,
    int RetentionDays,
    bool RegistrationOpen,
    OAuthClientPatch GithubOAuth,
    OAuthClientPatch SlackOAuth,
    OAuthClientPatch GoogleOAuth,
    OAuthClientPatch GitlabOAuth,
    OAuthClientPatch BitbucketOAuth,
    OAuthClientPatch ForgejoOAuth,
    OAuthClientPatch GiteaOAuth,
    OAuthClientPatch CodebergOAuth,
    string? OAuthRedirectBase
);

// Same preserve-on-null contract as OidcClientSecret — empty string means "clear". Host is
// only honoured for Forgejo + Gitea; ignored for SaaS providers.
public sealed record OAuthClientPatch(
    string? ClientId,
    string? ClientSecret,
    bool PreserveClientSecret,
    string? Scopes,
    string? Host = null
);
