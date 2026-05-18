namespace Shield.Api.Services.Updates;

// SignalR + Notification fan-out for live Updates-apply progress. Each broadcast call mirrors
// to (a) every connected `findings` hub client as a `updates.*` event, (b) for terminal events,
// an inbox Notification so users who weren't watching still see the result.
public interface IUpdateApplyBroadcaster
{
    Task JobStartedAsync(Guid jobId, int totalSources, CancellationToken ct);
    Task SourceCompletedAsync(Guid jobId, SourceApplyOutcome outcome, CancellationToken ct);
    Task JobCompletedAsync(
        Guid jobId,
        Guid? requestedByUserId,
        UpdateApplyResult result,
        CancellationToken ct
    );
    Task JobFailedAsync(Guid jobId, Guid? requestedByUserId, string message, CancellationToken ct);
}
