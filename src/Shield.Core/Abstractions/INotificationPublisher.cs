using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

// Publishes a user-targeted or broadcast notification. Implemented in Shield.Api;
// consumed here in Shield.Core / Shield.Channels to avoid an upward dependency.
public interface INotificationPublisher
{
    Task PublishAsync(Notification notification, CancellationToken ct = default);

    Task BroadcastAsync(
        NotificationKind kind,
        Severity severity,
        string title,
        string body,
        string? relatedType = null,
        string? relatedId = null,
        CancellationToken ct = default
    );
}
