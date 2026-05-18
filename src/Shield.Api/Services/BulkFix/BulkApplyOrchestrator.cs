using System.Globalization;

namespace Shield.Api.Services.BulkFix;

public sealed class BulkApplyOrchestrator : IBulkApplyOrchestrator
{
    private static readonly TimeSpan ManualCooldown = TimeSpan.FromHours(24);

    private readonly ShieldDbContext _db;
    private readonly FeedsDbContext _feedsDb;
    private readonly IBulkFixApplier _bulkFixApplier;
    private readonly IAuditLogger _audit;
    private readonly INotificationPublisher _notifications;
    private readonly ISecurityEventLogger _securityLog;
    private readonly IAdminAudienceProvider _adminAudience;

    public BulkApplyOrchestrator(
        ShieldDbContext db,
        FeedsDbContext feedsDb,
        IBulkFixApplier bulkFixApplier,
        IAuditLogger audit,
        INotificationPublisher notifications,
        ISecurityEventLogger securityLog,
        IAdminAudienceProvider adminAudience
    )
    {
        _db = db;
        _feedsDb = feedsDb;
        _bulkFixApplier = bulkFixApplier;
        _audit = audit;
        _notifications = notifications;
        _securityLog = securityLog;
        _adminAudience = adminAudience;
    }

    public async Task<BulkApplyDispatchResult> ApplyAsync(
        int sourceId,
        BulkApplyRequest request,
        CancellationToken ct
    )
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == sourceId, ct);
        if (source is null)
            return new(BulkApplyOutcome.SourceNotFound);

        if (source.Type != SourceType.GithubRepo)
            return new(
                BulkApplyOutcome.UnsupportedType,
                ErrorCode: "apply_unsupported_type",
                ErrorMessage: "Bulk apply only supports GithubRepo sources."
            );

        if (source.IsProduction && !request.DryRun && !request.ConfirmProduction)
            return new(
                BulkApplyOutcome.ProductionConfirmationRequired,
                ErrorCode: "production_source_confirmation_required",
                ErrorMessage: "Source is marked production. Re-submit with confirmProduction: true to proceed."
            );

        // 24h manual cooldown — gates spam clicks, NOT the scheduler. Escape hatches:
        //   1. force=true — admin override
        //   2. New open findings since last manual apply — a fresh advisory must never be
        //      blocked by yesterday's apply.
        if (!request.DryRun && !request.Force && source.LastManualBulkApplyAt.HasValue)
        {
            TimeSpan elapsed = DateTime.UtcNow - source.LastManualBulkApplyAt.Value;
            if (elapsed < ManualCooldown)
            {
                bool hasNewFindings = await _db.Findings.AnyAsync(
                    finding =>
                        finding.SourceId == sourceId
                        && finding.State == FindingState.Open
                        && finding.FirstSeenAt > source.LastManualBulkApplyAt!.Value,
                    ct
                );
                if (!hasNewFindings)
                {
                    DateTime retryAfter = source.LastManualBulkApplyAt.Value + ManualCooldown;
                    return new(
                        BulkApplyOutcome.Cooldown,
                        ErrorCode: "bulk_cooldown",
                        ErrorMessage: $"Bulk apply was run recently. Wait until {retryAfter:u} or pass force=true to bypass.",
                        RetryAfter: retryAfter
                    );
                }
            }
        }

        IReadOnlyList<Advisory> advisories = await LoadAdvisoriesAsync(sourceId, ct);

        BulkApplyResult result = await _bulkFixApplier.ApplyAllPullRequestAsync(
            source,
            advisories,
            request.DryRun,
            request.MaxPackages,
            request.AllowMajorBumps,
            ct
        );

        if (!request.DryRun && result.PullRequestUrl is not null)
            await RecordSuccessAsync(source, result, ct);

        BulkApplyResponse response = new(
            DryRun: result.DryRun,
            PullRequestUrl: result.PullRequestUrl,
            Entries: result.Entries,
            Errors: result.Errors,
            ReusedBranch: result.ReusedBranch,
            MajorBumps: result.MajorBumps,
            Warnings: result.Warnings
        );
        return new(BulkApplyOutcome.Ok, Response: response);
    }

    private async Task<IReadOnlyList<Advisory>> LoadAdvisoriesAsync(
        int sourceId,
        CancellationToken ct
    )
    {
        List<Finding> openFindings = await _db
            .Findings.Where(finding =>
                finding.SourceId == sourceId && finding.State == FindingState.Open
            )
            .ToListAsync(ct);
        if (openFindings.Count == 0)
            return [];

        HashSet<Guid> advisoryIds = openFindings.Select(f => f.AdvisoryRefId).ToHashSet();
        return await _feedsDb
            .Advisories.Where(advisory => advisoryIds.Contains(advisory.Id))
            .ToListAsync(ct);
    }

    private async Task RecordSuccessAsync(
        Source source,
        BulkApplyResult result,
        CancellationToken ct
    )
    {
        // Manual path: write the manual column only. Scheduler maintains LastBulkApplyAt.
        source.LastManualBulkApplyAt = DateTime.UtcNow;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "source.bulk_apply",
            "Source",
            source.Id.ToString(CultureInfo.InvariantCulture),
            details: new
            {
                packages = result.Entries.Select(entry => entry.PackageName).ToList(),
                prUrl = result.PullRequestUrl,
                count = result.Entries.Count,
            },
            ct
        );

        IReadOnlyList<Guid> adminIds = await _adminAudience.GetAdminUserIdsAsync(ct);
        string title = $"Shield opened a fix PR for {source.Name}";
        string body =
            $"{result.Entries.Count} packages bumped — {result.MajorBumps.Count} majors held back. Click to review.";
        foreach (Guid adminId in adminIds)
        {
            await _notifications.PublishAsync(
                new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = adminId,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = title,
                    Body = body,
                    RelatedType = "PullRequest",
                    RelatedId = result.PullRequestUrl,
                    CreatedAt = DateTime.UtcNow,
                },
                ct
            );
        }

        await _securityLog.LogAsync(
            source: "shield.fix",
            eventType: "fix.bulk_pr_opened",
            severity: Severity.Low,
            path: $"/api/sources/{source.Id}/apply-all-fixes",
            ct: ct
        );
    }
}
