using Shield.Core.Domain;

namespace Shield.Core.Results;

public sealed record FeedSyncResult(
    int AdvisoriesIngested,
    int AdvisoriesUpdated,
    string? NextCursor,
    bool Success,
    string? Error
)
{
    public static FeedSyncResult Ok(int ingested, int updated, string? nextCursor) =>
        new(ingested, updated, nextCursor, true, null);

    public static FeedSyncResult Fail(string error, string? lastCursor = null) =>
        new(0, 0, lastCursor, false, error);
}
