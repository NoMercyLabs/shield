namespace Shield.Api.Services.Auth;

// Marker interface so tests can resolve and trigger a manual sweep without waiting on the
// hourly tick. The hosted background service implementation lives in OauthExpiryWatcher.
public interface IOauthExpiryWatcher
{
    Task SweepAsync(CancellationToken ct);
}
