using Microsoft.Extensions.Options;
using Shield.Core.Domain;
using Shield.Feeds.Ghsa;

namespace Shield.Api.Services.Feeds;

// Resolves a GitHub bearer token for the GHSA feed sync from any of the following, in
// priority order:
//   1. Dashboard-set PAT (Settings → Feeds, persisted encrypted in AppSettings).
//   2. Shield:Feeds:Ghsa:Pat config value — legacy boot-time override, kept so existing
//      env-var installs don't break. New installs should use the dashboard.
//   3. The OAuth "connect-flow" GitHub token persisted by the SPA (Subject == "").
//   4. Any signin GitHub OAuth token belonging to a logged-in user — admins logging in via
//      GitHub leave this behind and it raises the GHSA quota for everyone.
//
// Returns null only when all four are empty. Falling through to anonymous (60/hr) is
// preferable to hard-failing the feed sync — the public securityAdvisories query works
// without auth, just slowly.
public sealed class OAuthBackedGhsaTokenSource : IGhsaAuthTokenSource
{
    private readonly IOAuthTokenStore _tokens;
    private readonly IOptionsMonitor<GhsaOptions> _options;
    private readonly IAppSettingsService _appSettings;
    private readonly ILogger<OAuthBackedGhsaTokenSource> _log;

    public OAuthBackedGhsaTokenSource(
        IOAuthTokenStore tokens,
        IOptionsMonitor<GhsaOptions> options,
        IAppSettingsService appSettings,
        ILogger<OAuthBackedGhsaTokenSource> log
    )
    {
        _tokens = tokens;
        _options = options;
        _appSettings = appSettings;
        _log = log;
    }

    public async ValueTask<string?> GetTokenAsync(CancellationToken ct = default)
    {
        (string? token, _) = await ResolveAsync(ct);
        return token;
    }

    public async ValueTask<GhsaTokenStatus> GetStatusAsync(CancellationToken ct = default)
    {
        (_, GhsaTokenStatus status) = await ResolveAsync(ct);
        return status;
    }

    private async ValueTask<(string? Token, GhsaTokenStatus Status)> ResolveAsync(
        CancellationToken ct
    )
    {
        string? dashboardPat = await _appSettings.GetStringAsync(AppSettingKeys.GhsaPat, ct);
        if (!string.IsNullOrWhiteSpace(dashboardPat))
            return (dashboardPat, new GhsaTokenStatus(GhsaTokenOrigin.DashboardPat));

        string? configPat = _options.CurrentValue.Pat;
        if (!string.IsNullOrWhiteSpace(configPat))
            return (configPat, new GhsaTokenStatus(GhsaTokenOrigin.ConfigPat));

        OAuthTokenSnapshot? connect = await _tokens.GetAsync(OAuthProvider.Github, ct);
        if (connect is not null && !string.IsNullOrWhiteSpace(connect.AccessToken))
        {
            _log.LogDebug(
                "GHSA bearer resolved from OAuth connect-flow ({Login})",
                connect.AccountLogin
            );
            return (
                connect.AccessToken,
                new GhsaTokenStatus(GhsaTokenOrigin.OAuthConnect, connect.AccountLogin)
            );
        }

        OAuthTokenSnapshot? signin = await _tokens.GetAnyAsync(OAuthProvider.Github, ct);
        if (signin is not null && !string.IsNullOrWhiteSpace(signin.AccessToken))
        {
            _log.LogDebug("GHSA bearer resolved from OAuth signin ({Login})", signin.AccountLogin);
            return (
                signin.AccessToken,
                new GhsaTokenStatus(GhsaTokenOrigin.OAuthSignin, signin.AccountLogin)
            );
        }

        return (null, new GhsaTokenStatus(GhsaTokenOrigin.None));
    }
}
