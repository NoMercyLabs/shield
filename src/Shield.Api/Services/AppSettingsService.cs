using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shield.Core.Domain;
using Shield.Core.Options;
using Shield.Data;

namespace Shield.Api.Services;

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
    string? OAuthRedirectBase,
    IReadOnlyList<string> DetectedRemoteHosts
);

// ClientId/Scopes are plain strings; ClientSecret is held decrypted in-memory only
// (loaded via the same DataProtector that encrypts AppSetting rows).
public sealed record OAuthClientSettings(string? ClientId, string? ClientSecret, string? Scopes);

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
    string? OAuthRedirectBase
);

// Same preserve-on-null contract as OidcClientSecret — empty string means "clear".
public sealed record OAuthClientPatch(
    string? ClientId,
    string? ClientSecret,
    bool PreserveClientSecret,
    string? Scopes
);

public static class AppSettingKeys
{
    public const string SingleUserMode = "singleUserMode";
    public const string OpenApiEnabled = "openApiEnabled";
    public const string OidcEnabled = "oidcEnabled";
    public const string OidcIssuer = "oidcIssuer";
    public const string OidcClientId = "oidcClientId";
    public const string OidcClientSecret = "oidcClientSecret";
    public const string AlertSeverityFloor = "alertSeverityFloor";
    public const string RetentionDays = "retentionDays";
    public const string RegistrationOpen = "registrationOpen";

    public const string OnboardingDismissed = "onboarding.dismissed";

    public const string OAuthRedirectBase = "oauth.redirectBase";
    public const string GithubOAuthClientId = "oauth.github.clientId";
    public const string GithubOAuthClientSecret = "oauth.github.clientSecret";
    public const string GithubOAuthScopes = "oauth.github.scopes";
    public const string SlackOAuthClientId = "oauth.slack.clientId";
    public const string SlackOAuthClientSecret = "oauth.slack.clientSecret";
    public const string SlackOAuthScopes = "oauth.slack.scopes";
    public const string GoogleOAuthClientId = "oauth.google.clientId";
    public const string GoogleOAuthClientSecret = "oauth.google.clientSecret";
    public const string GoogleOAuthScopes = "oauth.google.scopes";
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;
    private readonly IOptions<ShieldOptions> _shieldOptions;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private AppSettingsSnapshot? _current;

    public AppSettingsService(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider protectionProvider,
        IOptions<ShieldOptions> shieldOptions,
        IConfiguration configuration
    )
    {
        _scopeFactory = scopeFactory;
        _protector = protectionProvider.CreateProtector("shield.settings");
        _shieldOptions = shieldOptions;
        _configuration = configuration;
    }

    public AppSettingsSnapshot Current => _current ?? Defaults();

    public event Action<AppSettingsSnapshot>? Changed;

    public async Task<AppSettingsSnapshot> GetAsync(CancellationToken ct = default)
    {
        if (_current is not null)
            return _current;
        await _writeLock.WaitAsync(ct);
        try
        {
            _current ??= await LoadAsync(ct);
            return _current;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AppSettingsSnapshot> ReloadAsync(CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            AppSettingsSnapshot snapshot = await LoadAsync(ct);
            _current = snapshot;
            Changed?.Invoke(snapshot);
            return snapshot;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AppSettingsSnapshot> UpdateAsync(
        AppSettingsPatch patch,
        Guid? updatedBy,
        CancellationToken ct = default
    )
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Dictionary<string, AppSetting> rows = await db.AppSettings.ToDictionaryAsync(
                row => row.Key,
                StringComparer.Ordinal,
                ct
            );

            DateTime now = DateTime.UtcNow;

            void Write(string key, string value)
            {
                string encrypted = _protector.Protect(value);
                if (rows.TryGetValue(key, out AppSetting? existing))
                {
                    existing.ValueEncrypted = encrypted;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = updatedBy;
                }
                else
                {
                    db.AppSettings.Add(
                        new AppSetting
                        {
                            Key = key,
                            ValueEncrypted = encrypted,
                            UpdatedAt = now,
                            UpdatedBy = updatedBy,
                        }
                    );
                }
            }

            Write(AppSettingKeys.SingleUserMode, patch.SingleUserMode ? "true" : "false");
            Write(AppSettingKeys.OpenApiEnabled, patch.OpenApiEnabled ? "true" : "false");
            Write(AppSettingKeys.OidcEnabled, patch.OidcEnabled ? "true" : "false");
            Write(AppSettingKeys.OidcIssuer, patch.OidcIssuer ?? "");
            Write(AppSettingKeys.OidcClientId, patch.OidcClientId ?? "");
            Write(AppSettingKeys.AlertSeverityFloor, patch.AlertSeverityFloor.ToString());
            Write(AppSettingKeys.RetentionDays, patch.RetentionDays.ToString());
            Write(AppSettingKeys.RegistrationOpen, patch.RegistrationOpen ? "true" : "false");
            Write(AppSettingKeys.OAuthRedirectBase, patch.OAuthRedirectBase ?? "");

            WriteOAuthClient(
                patch.GithubOAuth,
                AppSettingKeys.GithubOAuthClientId,
                AppSettingKeys.GithubOAuthClientSecret,
                AppSettingKeys.GithubOAuthScopes
            );
            WriteOAuthClient(
                patch.SlackOAuth,
                AppSettingKeys.SlackOAuthClientId,
                AppSettingKeys.SlackOAuthClientSecret,
                AppSettingKeys.SlackOAuthScopes
            );
            WriteOAuthClient(
                patch.GoogleOAuth,
                AppSettingKeys.GoogleOAuthClientId,
                AppSettingKeys.GoogleOAuthClientSecret,
                AppSettingKeys.GoogleOAuthScopes
            );

            if (!patch.PreserveOidcClientSecret)
                Write(AppSettingKeys.OidcClientSecret, patch.OidcClientSecret ?? "");

            void WriteOAuthClient(
                OAuthClientPatch clientPatch,
                string clientIdKey,
                string secretKey,
                string scopesKey
            )
            {
                Write(clientIdKey, clientPatch.ClientId ?? "");
                Write(scopesKey, clientPatch.Scopes ?? "");
                if (!clientPatch.PreserveClientSecret)
                    Write(secretKey, clientPatch.ClientSecret ?? "");
            }

            await db.SaveChangesAsync(ct);

            AppSettingsSnapshot snapshot = await LoadAsync(ct);
            _current = snapshot;
            Changed?.Invoke(snapshot);
            return snapshot;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<AppSettingsSnapshot> LoadAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        List<AppSetting> rows = await db.AppSettings.ToListAsync(ct);
        Dictionary<string, string> stored = new(StringComparer.Ordinal);
        foreach (AppSetting row in rows)
        {
            try
            {
                stored[row.Key] = _protector.Unprotect(row.ValueEncrypted);
            }
            catch
            {
                // Unreadable row (rotated key/corrupt) — fall back to empty so the page still loads.
                stored[row.Key] = "";
            }
        }

        bool singleUser = ReadBool(
            stored,
            AppSettingKeys.SingleUserMode,
            _shieldOptions.Value.SingleUser
        );
        bool openApi = ReadBool(
            stored,
            AppSettingKeys.OpenApiEnabled,
            _configuration.GetValue("Shield:OpenApi:Enabled", false)
        );
        bool oidcEnabled = ReadBool(
            stored,
            AppSettingKeys.OidcEnabled,
            _configuration.GetValue("Shield:Oidc:Enabled", false)
        );

        string? issuer =
            ReadString(stored, AppSettingKeys.OidcIssuer) ?? _configuration["Shield:Oidc:Issuer"];
        string? clientId =
            ReadString(stored, AppSettingKeys.OidcClientId)
            ?? _configuration["Shield:Oidc:ClientId"];
        string? secret = ReadString(stored, AppSettingKeys.OidcClientSecret);

        Severity floor = Severity.Low;
        if (
            stored.TryGetValue(AppSettingKeys.AlertSeverityFloor, out string? floorRaw)
            && Enum.TryParse(floorRaw, ignoreCase: true, out Severity parsed)
        )
        {
            floor = parsed;
        }

        int retention = 90;
        if (
            stored.TryGetValue(AppSettingKeys.RetentionDays, out string? retentionRaw)
            && int.TryParse(retentionRaw, out int parsedDays)
        )
        {
            retention = parsedDays;
        }

        bool registrationOpen = ReadBool(
            stored,
            AppSettingKeys.RegistrationOpen,
            _configuration.GetValue("Shield:RegistrationOpen", false)
        );

        OAuthClientSettings githubOAuth = ReadOAuthClient(
            stored,
            AppSettingKeys.GithubOAuthClientId,
            AppSettingKeys.GithubOAuthClientSecret,
            AppSettingKeys.GithubOAuthScopes,
            _configuration["Shield:OAuth:Github:ClientId"],
            _configuration["Shield:OAuth:Github:ClientSecret"]
        );
        OAuthClientSettings slackOAuth = ReadOAuthClient(
            stored,
            AppSettingKeys.SlackOAuthClientId,
            AppSettingKeys.SlackOAuthClientSecret,
            AppSettingKeys.SlackOAuthScopes,
            _configuration["Shield:OAuth:Slack:ClientId"],
            _configuration["Shield:OAuth:Slack:ClientSecret"]
        );
        OAuthClientSettings googleOAuth = ReadOAuthClient(
            stored,
            AppSettingKeys.GoogleOAuthClientId,
            AppSettingKeys.GoogleOAuthClientSecret,
            AppSettingKeys.GoogleOAuthScopes,
            _configuration["Shield:OAuth:Google:ClientId"],
            _configuration["Shield:OAuth:Google:ClientSecret"]
        );
        string? redirectBase =
            ReadString(stored, AppSettingKeys.OAuthRedirectBase)
            ?? _configuration["Shield:OAuth:RedirectBase"];

        string detectedHostsRaw =
            _configuration["Shield:Scanners:DetectedRemoteHosts"]
            ?? "github.com,gitlab.com,bitbucket.org";
        IReadOnlyList<string> detectedHosts = ParseHostList(detectedHostsRaw);

        return new AppSettingsSnapshot(
            singleUser,
            openApi,
            oidcEnabled,
            string.IsNullOrEmpty(issuer) ? null : issuer,
            string.IsNullOrEmpty(clientId) ? null : clientId,
            string.IsNullOrEmpty(secret) ? null : secret,
            floor,
            retention,
            registrationOpen,
            githubOAuth,
            slackOAuth,
            googleOAuth,
            string.IsNullOrEmpty(redirectBase) ? null : redirectBase,
            detectedHosts
        );
    }

    private static IReadOnlyList<string> ParseHostList(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(host => host.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static OAuthClientSettings ReadOAuthClient(
        Dictionary<string, string> stored,
        string clientIdKey,
        string secretKey,
        string scopesKey,
        string? clientIdFallback,
        string? clientSecretFallback
    )
    {
        string? clientId = ReadString(stored, clientIdKey) ?? clientIdFallback;
        string? secret = ReadString(stored, secretKey) ?? clientSecretFallback;
        string? scopes = ReadString(stored, scopesKey);
        return new OAuthClientSettings(
            string.IsNullOrEmpty(clientId) ? null : clientId,
            string.IsNullOrEmpty(secret) ? null : secret,
            string.IsNullOrEmpty(scopes) ? null : scopes
        );
    }

    private AppSettingsSnapshot Defaults() =>
        new(
            _shieldOptions.Value.SingleUser,
            _configuration.GetValue("Shield:OpenApi:Enabled", false),
            _configuration.GetValue("Shield:Oidc:Enabled", false),
            _configuration["Shield:Oidc:Issuer"],
            _configuration["Shield:Oidc:ClientId"],
            null,
            Severity.Low,
            90,
            _configuration.GetValue("Shield:RegistrationOpen", false),
            new OAuthClientSettings(_configuration["Shield:OAuth:Github:ClientId"], null, null),
            new OAuthClientSettings(_configuration["Shield:OAuth:Slack:ClientId"], null, null),
            new OAuthClientSettings(_configuration["Shield:OAuth:Google:ClientId"], null, null),
            _configuration["Shield:OAuth:RedirectBase"],
            ParseHostList(
                _configuration["Shield:Scanners:DetectedRemoteHosts"]
                    ?? "github.com,gitlab.com,bitbucket.org"
            )
        );

    private static bool ReadBool(Dictionary<string, string> map, string key, bool fallback)
    {
        if (map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed))
            return parsed;
        return fallback;
    }

    private static string? ReadString(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : null;
}
