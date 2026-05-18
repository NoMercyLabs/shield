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

    // Optional capability — providers that can enumerate the connected user's repos
    // override this so the SPA's "Pick repos" modal works without a provider-specific
    // endpoint. The default returns null so non-repo-hosting providers (Slack, Google,
    // SMTP) don't have to implement a method they have nothing to offer for.
    Task<IReadOnlyList<RepositorySummary>?> ListRepositoriesAsync(
        string accessToken,
        RepositoryListOptions options,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<RepositorySummary>?>(null);
}

public sealed record OAuthClientConfig(string ClientId, string ClientSecret, string RedirectUri);

// Normalized cross-provider shape used by the repo-picker modal. Each provider maps
// its native response (GitHubRepoEntry, GitlabProjectEntry, BitbucketRepository …) into
// this so the controller, contracts, and SPA all stay provider-agnostic.
public sealed record RepositorySummary(
    string Owner,
    string Name,
    string FullName,
    string? Description,
    string? DefaultBranch,
    bool IsPrivate,
    bool Archived,
    bool Fork,
    string? Language
);

public sealed record RepositoryListOptions(string? Affiliation, int PerPage, int MaxRepositories);

// Signin returns the provider's stable subject + claims used to find-or-create a local user,
// plus the same token snapshot we'd persist in the connect flow.
public sealed record OAuthSigninResult(
    string Subject,
    string Login,
    string? Email,
    OAuthTokenSnapshot Token
);
