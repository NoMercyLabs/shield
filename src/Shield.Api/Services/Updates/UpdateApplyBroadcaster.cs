using Microsoft.AspNetCore.SignalR;
using Shield.Api.Hubs;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Api.Services.Updates;

public sealed class UpdateApplyBroadcaster : IUpdateApplyBroadcaster
{
    private readonly IHubContext<FindingsHub> _hub;
    private readonly INotificationPublisher _notifications;

    public UpdateApplyBroadcaster(
        IHubContext<FindingsHub> hub,
        INotificationPublisher notifications
    )
    {
        _hub = hub;
        _notifications = notifications;
    }

    public Task JobStartedAsync(Guid jobId, int totalSources, CancellationToken ct) =>
        _hub.Clients.All.SendAsync("updates.job.started", new { jobId, totalSources }, ct);

    public Task SourceCompletedAsync(
        Guid jobId,
        SourceApplyOutcome outcome,
        CancellationToken ct
    ) =>
        _hub.Clients.All.SendAsync(
            "updates.source.completed",
            new
            {
                jobId,
                sourceId = outcome.SourceId,
                sourceName = outcome.SourceName,
                pullRequestUrl = outcome.PullRequestUrl,
                bumpedCount = outcome.BumpedCount,
                skippedYoungCount = outcome.SkippedYoungCount,
                skippedMajorCount = outcome.SkippedMajorCount,
                errors = outcome.Errors,
            },
            ct
        );

    public async Task JobCompletedAsync(
        Guid jobId,
        Guid? requestedByUserId,
        UpdateApplyResult result,
        CancellationToken ct
    )
    {
        int opened = result.Sources.Count(source => source.PullRequestUrl is not null);
        int failed = result.Sources.Count(source =>
            source.PullRequestUrl is null && source.Errors.Count > 0
        );
        // "Nothing to apply" sources — no PR, no errors, all rows filtered out (already applied,
        // too young, major-excluded by scope, unsupported ecosystem). These aren't failures,
        // they're a no-op and reading the notification shouldn't make it sound like one.
        int noop = result.Sources.Count - opened - failed;
        int bumpsApplied = result.Sources.Sum(source =>
            source.PullRequestUrl is not null ? source.BumpedCount : 0
        );

        await _hub.Clients.All.SendAsync(
            "updates.job.completed",
            new
            {
                jobId,
                opened,
                failed,
                noop,
                bumpsApplied,
                sourcesTotal = result.Sources.Count,
            },
            ct
        );

        (string title, string body, Severity severity) = BuildSummary(
            opened,
            failed,
            noop,
            bumpsApplied,
            result.Sources.Count
        );

        await _notifications.BroadcastAsync(
            NotificationKind.SystemMessage,
            severity,
            title,
            body,
            relatedType: "UpdateJob",
            relatedId: jobId.ToString(),
            ct
        );
    }

    private static (string Title, string Body, Severity Severity) BuildSummary(
        int opened,
        int failed,
        int noop,
        int bumpsApplied,
        int sourcesTotal
    )
    {
        // Branch on the outcome shape, not just the opened count, so "nothing to apply" doesn't
        // read like a failure.
        if (opened == 0 && failed == 0)
        {
            return (
                "Updates apply — nothing to do",
                $"All {sourcesTotal} source(s) had no eligible bumps (already applied, too young, or filtered by scope).",
                Severity.Low
            );
        }
        if (opened == 0)
        {
            return (
                "Updates apply — no PRs opened",
                $"{failed} source(s) reported errors; {noop} had nothing to apply. See /updates for details.",
                Severity.Medium
            );
        }
        string title =
            opened == 1 ? "Updates apply — 1 PR opened" : $"Updates apply — {opened} PRs opened";
        string body =
            failed > 0
                ? $"{bumpsApplied} package bump(s) across {opened} source(s); {failed} source(s) failed."
                : $"{bumpsApplied} package bump(s) across {opened} source(s).";
        return (title, body, Severity.Low);
    }

    public async Task JobFailedAsync(
        Guid jobId,
        Guid? requestedByUserId,
        string message,
        CancellationToken ct
    )
    {
        await _hub.Clients.All.SendAsync("updates.job.failed", new { jobId, message }, ct);

        await _notifications.BroadcastAsync(
            NotificationKind.SystemMessage,
            Severity.High,
            "Updates apply failed",
            message,
            relatedType: "UpdateJob",
            relatedId: jobId.ToString(),
            ct
        );
    }
}
