using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services.Auth;

// Background watcher that wakes hourly and emits an OauthExpiring notification for any
// IntegrationToken whose ExpiresAt is within 7 days. Hosted Singleton — owns no per-request
// state and resolves DbContext + publisher from a fresh scope on every tick. Per-row
// dedup is best-effort: we skip if an unread OauthExpiring notification for the same
// IntegrationToken Id already exists in the last 24h.
public sealed class OauthExpiryWatcher : BackgroundService, IOauthExpiryWatcher
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ExpiryWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OauthExpiryWatcher> _log;

    public OauthExpiryWatcher(IServiceScopeFactory scopeFactory, ILogger<OauthExpiryWatcher> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First sweep runs immediately so a freshly-booted server doesn't wait an hour to
        // emit notifications for tokens that expire today.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OAuth expiry sweep failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        INotificationPublisher publisher =
            scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

        DateTime now = DateTime.UtcNow;
        DateTime cutoff = now + ExpiryWindow;
        DateTime dedupSince = now - DedupWindow;

        List<IntegrationToken> expiring = await db
            .IntegrationTokens.AsNoTracking()
            .Where(token =>
                token.ExpiresAt != null && token.ExpiresAt <= cutoff && token.ExpiresAt > now
            )
            .ToListAsync(ct);

        if (expiring.Count == 0)
            return;

        // Pull the recent OauthExpiring notifications once and dedup in-memory.
        HashSet<string> recentRelatedIds = (
            await db
                .Notifications.AsNoTracking()
                .Where(notification =>
                    notification.Kind == NotificationKind.OauthExpiring
                    && notification.CreatedAt >= dedupSince
                    && notification.RelatedId != null
                )
                .Select(notification => notification.RelatedId!)
                .ToListAsync(ct)
        ).ToHashSet(StringComparer.Ordinal);

        foreach (IntegrationToken token in expiring)
        {
            string relatedId = token.Id.ToString();
            if (recentRelatedIds.Contains(relatedId))
                continue;

            DateTime expiresAt = token.ExpiresAt!.Value;
            int days = Math.Max(0, (int)Math.Ceiling((expiresAt - now).TotalDays));
            string title = $"{token.Provider} token expiring in {days} day{(days == 1 ? "" : "s")}";
            string body =
                $"OAuth token for {token.Provider} (account {token.AccountLogin}) expires at "
                + $"{expiresAt:yyyy-MM-dd HH:mm} UTC.";

            await publisher.BroadcastAsync(
                NotificationKind.OauthExpiring,
                Severity.Medium,
                title,
                body,
                relatedType: "OAuth",
                relatedId: relatedId,
                ct
            );
        }
    }
}
