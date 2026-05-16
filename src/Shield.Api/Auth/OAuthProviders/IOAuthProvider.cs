using Shield.Api.Services;
using Shield.Core.Domain;

namespace Shield.Api.Auth.OAuthProviders;

// One adapter per provider. Implementations encapsulate endpoint URLs, the
// authorization-code/PKCE exchange shape, and the user-identity probe.
public interface IOAuthProvider
{
    OAuthProvider Provider { get; }
    string DefaultScopes { get; }
    bool SupportsPkce { get; }

    // Builds the URL the browser should be sent to. State + verifier are caller-managed.
    string BuildAuthorizationUrl(
        OAuthClientConfig config,
        string state,
        string codeChallenge,
        string scopes
    );

    // Code-for-token exchange. Returns enough metadata to write IntegrationToken.
    Task<OAuthTokenSnapshot> ExchangeCodeAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    );

    // Optional best-effort token refresh. Returns null if the provider doesn't
    // expose refresh (e.g. GitHub user tokens with no refresh policy enabled).
    Task<OAuthTokenSnapshot?> RefreshAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot current,
        CancellationToken ct
    );

    // Best-effort revocation. Failures are logged but don't block local delete.
    Task RevokeAsync(OAuthClientConfig config, OAuthTokenSnapshot token, CancellationToken ct);

    // Signin variants — separate flow because some providers (Slack) use a different
    // endpoint/scopes when proving the user's identity vs. installing an app.
    string SigninDefaultScopes { get; }

    string BuildSigninAuthorizationUrl(
        OAuthClientConfig config,
        string state,
        string codeChallenge,
        string scopes
    );

    Task<OAuthSigninResult> ExchangeCodeForSigninAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    );
}

public sealed record OAuthClientConfig(string ClientId, string ClientSecret, string RedirectUri);

// Signin returns the provider's stable subject + claims used to find-or-create a local user,
// plus the same token snapshot we'd persist in the connect flow.
public sealed record OAuthSigninResult(
    string Subject,
    string Login,
    string? Email,
    Shield.Api.Services.OAuthTokenSnapshot Token
);
