namespace Shield.Core.Domain;

public class FeedSyncState
{
    public Guid Id { get; set; }
    public Feed Feed { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTime NextRunAt { get; set; }
    public string? Cursor { get; set; }

    // Set when the last sync was blocked by a rate-limit (e.g. GitHub 403 for unauthenticated
    // GraphQL). Cleared on the next successful sync. The SPA reads this to display a
    // "waiting for quota reset at HH:MM" message instead of a generic error.
    public DateTimeOffset? RateLimitResetAt { get; set; }
}
