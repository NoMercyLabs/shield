using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Core.Abstractions;

public interface IFeedSync
{
    Feed Feed { get; }
    ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct);
}
