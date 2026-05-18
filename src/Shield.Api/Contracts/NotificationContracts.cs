using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    Guid? UserId,
    NotificationKind Kind,
    Severity Severity,
    string Title,
    string Body,
    string? RelatedType,
    string? RelatedId,
    DateTime CreatedAt,
    DateTime? ReadAt,
    DateTime? ArchivedAt
)
{
    public static NotificationResponse From(Notification notification) =>
        new(
            notification.Id,
            notification.UserId,
            notification.Kind,
            notification.Severity,
            notification.Title,
            notification.Body,
            notification.RelatedType,
            notification.RelatedId,
            notification.CreatedAt,
            notification.ReadAt,
            notification.ArchivedAt
        );
}

public sealed record NotificationsPage(IReadOnlyList<NotificationResponse> Items, int UnreadCount);

public sealed record MarkAllReadResponse(int Updated);
