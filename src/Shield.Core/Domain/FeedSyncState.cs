namespace Shield.Core.Domain;

public class FeedSyncState
{
    public Guid Id { get; set; }
    public Feed Feed { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public DateTime NextRunAt { get; set; }
    public string? Cursor { get; set; }
}
