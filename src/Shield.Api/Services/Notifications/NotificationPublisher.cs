using Microsoft.AspNetCore.SignalR;
using Shield.Api.Hubs;

namespace Shield.Api.Services.Notifications;

// Scoped because it writes to ShieldDbContext (also scoped). Callers from background
// workers create their own scope (see SourceScanWorker/FeedSyncWorker) and resolve this
// from scope.ServiceProvider. SignalR's IHubContext is thread-safe singleton and fine to
// inject directly into a Scoped service.
public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly ShieldDbContext _db;
    private readonly IHubContext<FindingsHub> _hub;
    private readonly IWebPushSender _push;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(
        ShieldDbContext db,
        IHubContext<FindingsHub> hub,
        IWebPushSender push,
        ILogger<NotificationPublisher> logger
    )
    {
        _db = db;
        _hub = hub;
        _push = push;
        _logger = logger;
    }

    public async Task PublishAsync(Notification notification, CancellationToken ct = default)
    {
        if (notification.Id == Guid.Empty)
            notification.Id = Guid.NewGuid();
        if (notification.CreatedAt == default)
            notification.CreatedAt = DateTime.UtcNow;

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        // SignalR broadcasts to every connected client — clients filter on UserId themselves
        // (or treat null UserId as a broadcast). Per-user channels would require Identity
        // group joins which the current FindingsHub doesn't manage.
        await _hub.Clients.All.SendAsync(
            "notifications.new",
            NotificationResponse.From(notification),
            ct
        );

        // Web Push fan-out. Best-effort: any failure here must NOT roll back the in-app
        // notification — the bell + SignalR path is the source of truth.
        try
        {
            string url = BuildDeepLink(notification);
            PushPayload payload = new(
                Title: notification.Title,
                Body: notification.Body,
                Severity: notification.Severity.ToString(),
                Url: url,
                // Tag = notification id so a duplicate push (e.g. flaky network retry)
                // collapses into the existing notification instead of stacking.
                Tag: notification.Id.ToString()
            );
            await _push.DispatchAsync(payload, notification.UserId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Web push dispatch failed for notification {NotificationId}",
                notification.Id
            );
        }
    }

    public Task BroadcastAsync(
        NotificationKind kind,
        Severity severity,
        string title,
        string body,
        string? relatedType = null,
        string? relatedId = null,
        CancellationToken ct = default
    ) =>
        PublishAsync(
            new()
            {
                Id = Guid.NewGuid(),
                UserId = null,
                Kind = kind,
                Severity = severity,
                Title = title,
                Body = body,
                RelatedType = relatedType,
                RelatedId = relatedId,
                CreatedAt = DateTime.UtcNow,
            },
            ct
        );

    private static string BuildDeepLink(Notification notification)
    {
        // Map RelatedType to the SPA route. The push handler reads `data.url` and focuses
        // an existing window or opens a new one on that path.
        return notification.RelatedType switch
        {
            "Finding" when !string.IsNullOrEmpty(notification.RelatedId) =>
                $"/findings/{notification.RelatedId}",
            "Source" when !string.IsNullOrEmpty(notification.RelatedId) =>
                $"/sources/{notification.RelatedId}",
            "Feed" => "/feeds",
            "OAuth" => "/settings?tab=oauth",
            "Session" => "/account/sessions",
            "Invite" => "/access",
            _ => "/notifications",
        };
    }
}
