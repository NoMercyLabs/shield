using Shield.Core.Domain;

namespace Shield.Channels.Inbox;

public sealed class InboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAt { get; init; }
    public Severity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public Guid? FindingId { get; init; }
    public bool Read { get; init; }
}
