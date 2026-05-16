namespace Shield.Channels.Inbox;

public interface IInboxStore
{
    Task AddAsync(InboxMessage msg, CancellationToken ct);
}
