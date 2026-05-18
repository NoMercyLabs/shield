namespace Shield.Core.Domain;

// UserId null = broadcast (visible to every admin). PublisherBroadcastAsync writes a single
// row with UserId=null; per-user notifications use PublishAsync(notification, ct) with a
// concrete UserId. RelatedType/Id are free-form pointers ("Source" | "Finding" | "Feed" |
// "OAuth") so the UI can deep-link without coupling the table to a foreign key.
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public Severity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RelatedType { get; set; }
    public string? RelatedId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
