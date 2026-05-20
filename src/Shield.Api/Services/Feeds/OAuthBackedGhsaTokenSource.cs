using Microsoft.Extensions.Options;
using Shield.Api.Auth.OAuthProviders;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Feeds.Ghsa;

namespace Shield.Api.Services.Feeds;

// Resolves a GitHub bearer token for the GHSA feed sync from any of the following, in
// priority order:
//   1. Shield:Feeds:Ghsa:Pat config value — an explicit operator override.
//   2. The OAuth "connect-flow" GitHub token persisted by the SPA (Subject == "").
//      One per-install row, refreshed automatically by OAuth token rotation.
//   3. Any signin GitHub OAuth token belonging to a logged-in user. Looked up by
//      checking the first non-empty signin row — admins logging in via GitHub leave
//      this behind and it raises the GHSA quota for everyone.
//
// Returns null only when all three are empty. Falling through to anonymous (60/hr) is
// preferable to hard-failing the feed sync — the public securityAdvisories query works
// without auth, just slowly.
public sealed class OAuthBackedGhsaTokenSource : IGhsaAuthTokenSource
{
    private readonly IOAuthTokenStore _tokens;
    private readonly IOptionsMonitor<GhsaOptions> _options;
    private readonly ILogger<OAuthBackedGhsaTokenSource> _log;

    public OAuthBackedGhsaTokenSource(
        IOAuthTokenStore tokens,
        IOptionsMonitor<GhsaOptions> options,
        ILogger<OAuthBackedGhsaTokenSource> log
    )
    {
        _tokens = tokens;
        _options = options;
        _log = log;
    }

    public async ValueTask<string?> GetTokenAsync(CancellationToken ct = default)
    {
        string? pat = _options.CurrentValue.Pat;
        if (!string.IsNullOrWhiteSpace(pat))
            return pat;

        OAuthTokenSnapshot? connectFlow = await _tokens.GetAsync(OAuthProvider.Github, ct);
        if (connectFlow is not null && !string.IsNullOrWhiteSpace(connectFlow.AccessToken))
        {
            _log.LogDebug("GHSA bearer resolved from OAuth connect-flow token");
            return connectFlow.AccessToken;
        }

        return null;
    }
}
