using Shield.Channels.Inbox;

namespace Shield.Api.Persistence;

public sealed class InboxStore : IInboxStore
{
    private readonly InboxDbContext _db;

    public InboxStore(InboxDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(InboxMessage msg, CancellationToken ct)
    {
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync(ct);
    }
}
