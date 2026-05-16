using Shield.Api.Auth.OAuthProviders;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Api.Services;

// Singleton — scanners and feed workers ask for a fresh token; we refresh proactively
// when ExpiresAt is within RefreshLeeway. The accessor returns null when the provider
// isn't connected so callers can fall back to legacy config (PAT/appsettings).
public sealed class OAuthTokenAccessor : IOAuthTokenAccessor
{
    public static readonly TimeSpan RefreshLeeway = TimeSpan.FromMinutes(5);

    private readonly IOAuthTokenStore _store;
    private readonly IOAuthProviderRegistry _registry;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<OAuthTokenAccessor> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public OAuthTokenAccessor(
        IOAuthTokenStore store,
        IOAuthProviderRegistry registry,
        IAppSettingsService settings,
        ILogger<OAuthTokenAccessor> logger
    )
    {
        _store = store;
        _registry = registry;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(
        OAuthProvider provider,
        CancellationToken ct = default
    )
    {
        OAuthTokenSnapshot? current = await _store.GetAsync(provider, ct);
        if (current is null)
            return null;

        if (!ShouldRefresh(current))
            return current.AccessToken;

        if (!_registry.TryResolve(provider, out IOAuthProvider adapter))
            return current.AccessToken;

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        OAuthClientSettings client = ProviderConfig(snapshot, provider);
        if (string.IsNullOrEmpty(client.ClientId) || string.IsNullOrEmpty(client.ClientSecret))
            return current.AccessToken;

        string redirectBase = (snapshot.OAuthRedirectBase ?? "http://localhost:8080").TrimEnd('/');
        OAuthClientConfig config = new(
            client.ClientId!,
            client.ClientSecret!,
            $"{redirectBase}/api/oauth/{provider.ToString().ToLowerInvariant()}/callback"
        );

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Re-read in case another call already refreshed.
            current = await _store.GetAsync(provider, ct);
            if (current is null)
                return null;
            if (!ShouldRefresh(current))
                return current.AccessToken;

            OAuthTokenSnapshot? refreshed = await adapter.RefreshAsync(config, current, ct);
            if (refreshed is null)
                return current.AccessToken;

            await _store.SaveAsync(refreshed, ct);
            return refreshed.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Token refresh failed for {Provider}; using stale token",
                provider
            );
            return current?.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool ShouldRefresh(OAuthTokenSnapshot token)
    {
        if (token.ExpiresAt is null)
            return false;
        return token.ExpiresAt.Value - DateTime.UtcNow < RefreshLeeway;
    }

    private static OAuthClientSettings ProviderConfig(
        AppSettingsSnapshot snapshot,
        OAuthProvider provider
    ) =>
        provider switch
        {
            OAuthProvider.Github => snapshot.GithubOAuth,
            OAuthProvider.Slack => snapshot.SlackOAuth,
            OAuthProvider.Google => snapshot.GoogleOAuth,
            _ => new OAuthClientSettings(null, null, null),
        };
}
