namespace Shield.Core.Abstractions;

/// Resolves a GitHub bearer token for the GHSA GraphQL client. The default implementation
/// reads <c>Shield:Feeds:Ghsa:Pat</c> from config; Shield.Api overrides it with a resolver
/// that ALSO falls back to whatever GitHub OAuth token has been stored via the SPA's "connect
/// GitHub" flow OR a signin token from a logged-in admin. GHSA's <c>securityAdvisories</c>
/// query is public data — any authenticated GitHub identity raises the quota from 60/hr to
/// 5000/hr without needing extra scopes.
///
/// Returns <c>null</c> when no token is available; callers should still issue the request
/// unauthenticated rather than failing — the GHSA endpoint accepts anonymous traffic, just
/// at the lower quota.
public interface IGhsaAuthTokenSource
{
    ValueTask<string?> GetTokenAsync(CancellationToken ct = default);
}
