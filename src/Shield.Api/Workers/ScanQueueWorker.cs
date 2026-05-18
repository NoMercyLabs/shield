using Microsoft.EntityFrameworkCore;
using Shield.Api.Services;
using Shield.Api.Services.BulkFix;
using Shield.Api.Workers.Queues;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using Shield.Scanners;

// IBulkFixApplier is Scoped; workers create their own scope before resolving.

namespace Shield.Api.Workers;

// Drains the persistent ScanQueueEntries table one row at a time. Picks the oldest
// pending row OR an in-progress row whose StartedAt is older than StaleAfter (treated as
// a crashed worker — re-claimed on the next tick). Per-source serialisation prevents two
// concurrent scans of the same Source even after a crash-recover.
//
// Polling cadence is deliberately tight (2s) because bulk-add bursts wake a wide queue at
// once; with one row per scan and a 1s+ wait per row this would otherwise lag visibly.
public sealed class ScanQueueWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchQueue _matchQueue;
    private readonly ILogger<ScanQueueWorker> _log;
    private readonly HashSet<int> _inFlightSourceIds = [];
    private readonly object _inFlightLock = new();

    public ScanQueueWorker(
        IServiceScopeFactory scopeFactory,
        MatchQueue matchQueue,
        ILogger<ScanQueueWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _matchQueue = matchQueue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool processed = await TryDrainOneAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ScanQueueWorker drain loop crashed; will retry");
                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> TryDrainOneAsync(CancellationToken ct)
    {
        ScanQueueEntry? claimed = await ClaimNextAsync(ct);
        if (claimed is null)
            return false;

        try
        {
            await RunScanAsync(claimed, ct);
            return true;
        }
        finally
        {
            lock (_inFlightLock)
                _inFlightSourceIds.Remove(claimed.SourceId);
        }
    }

    private async Task<ScanQueueEntry?> ClaimNextAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        DateTime now = DateTime.UtcNow;
        DateTime staleCutoff = now - StaleAfter;

        // Snapshot in-flight set so the EF query can filter against it without holding the lock
        // across an await. The set is mutated in TryDrainOneAsync; copy is cheap (handful of ints).
        HashSet<int> excluded;
        lock (_inFlightLock)
            excluded = [.. _inFlightSourceIds];

        // "Pending" = never started, OR started >15m ago and never completed (crashed worker).
        // Deferred rows (DeferredUntil > now) are skipped until the rate-limit window expires.
        IQueryable<ScanQueueEntry> candidates = db
            .ScanQueueEntries.Where(entry =>
                entry.CompletedAt == null
                && (entry.StartedAt == null || entry.StartedAt < staleCutoff)
                && !excluded.Contains(entry.SourceId)
                && (entry.DeferredUntil == null || entry.DeferredUntil < now)
            )
            .OrderBy(entry => entry.EnqueuedAt);

        ScanQueueEntry? entry = await candidates.FirstOrDefaultAsync(ct);
        if (entry is null)
            return null;

        // Reserve the source before we commit StartedAt — another worker thread on the same
        // process would otherwise race in here on the same row.
        lock (_inFlightLock)
        {
            if (!_inFlightSourceIds.Add(entry.SourceId))
                return null;
        }

        entry.StartedAt = now;
        entry.Attempts++;
        try
        {
            await db.SaveChangesAsync(ct);
            return entry;
        }
        catch
        {
            lock (_inFlightLock)
                _inFlightSourceIds.Remove(entry.SourceId);
            throw;
        }
    }

    private async Task RunScanAsync(ScanQueueEntry queueEntry, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        ScannerRegistry registry = scope.ServiceProvider.GetRequiredService<ScannerRegistry>();

        ScanQueueEntry? tracked = await db.ScanQueueEntries.FirstOrDefaultAsync(
            row => row.Id == queueEntry.Id,
            ct
        );
        if (tracked is null)
            return;

        Source? source = await db.Sources.FirstOrDefaultAsync(s => s.Id == tracked.SourceId, ct);
        if (source is null)
        {
            tracked.CompletedAt = DateTime.UtcNow;
            tracked.ErrorMessage = "Source no longer exists";
            await db.SaveChangesAsync(ct);
            return;
        }

        IScanner? scanner = registry.FindFor(source.Type);
        if (scanner is null)
        {
            string error = $"No scanner registered for {source.Type}";
            source.LastError = error;
            tracked.ErrorMessage = error;
            tracked.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            ScanResult result = await scanner.ScanAsync(source, ct);
            source.LastScannedAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;

            if (!result.Success || result.Snapshot is null)
            {
                string error = result.Error ?? "Scan failed";
                source.LastError = error;
                tracked.ErrorMessage = error;
                tracked.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                if (tracked.Attempts >= MaxAttempts)
                {
                    INotificationPublisher publisher =
                        scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
                    await publisher.BroadcastAsync(
                        NotificationKind.ScanFailed,
                        Severity.Medium,
                        $"Scan failed: {source.Name}",
                        error,
                        relatedType: "Source",
                        relatedId: source.Id.ToString(),
                        ct
                    );
                }
                return;
            }

            source.LastError = null;
            db.InventorySnapshots.Add(result.Snapshot);
            if (result.Items.Count > 0)
                db.InventoryItems.AddRange(result.Items);
            tracked.CompletedAt = DateTime.UtcNow;
            tracked.ErrorMessage = null;
            await db.SaveChangesAsync(ct);

            await _matchQueue.EnqueueAsync(new(result.Snapshot.Id, source.Id, MatchAll: false), ct);

            IAnomalyDetector anomalyDetector =
                scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
            await anomalyDetector.AnalyzeNewSnapshotAsync(source.Id, result.Snapshot.Id, ct);

            await MaybeAutoApplyAsync(scope, source, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Don't mark complete — let the stale-recover path requeue when the host comes back.
            throw;
        }
        catch (GitHubScanRateLimitedException ex)
        {
            _log.LogInformation(
                "GitHub rate-limited for source {SourceId}; deferring until {RetryAt:u}",
                source.Id,
                ex.RetryAt
            );
            tracked.DeferredUntil = ex.RetryAt.UtcDateTime;
            tracked.StartedAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Scan threw for source {SourceId} (queue entry {EntryId}, attempt {Attempt})",
                source.Id,
                tracked.Id,
                tracked.Attempts
            );
            tracked.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            source.LastError = tracked.ErrorMessage;
            // Final-attempt failure marks the row complete so it doesn't loop forever.
            if (tracked.Attempts >= MaxAttempts)
            {
                tracked.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                try
                {
                    INotificationPublisher publisher =
                        scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
                    await publisher.BroadcastAsync(
                        NotificationKind.ScanFailed,
                        Severity.Medium,
                        $"Scan failed: {source.Name}",
                        tracked.ErrorMessage ?? "Scan threw",
                        relatedType: "Source",
                        relatedId: source.Id.ToString(),
                        ct
                    );
                }
                catch (Exception notifyEx)
                {
                    _log.LogWarning(notifyEx, "Failed to publish scan-failure notification");
                }
            }
            else
            {
                // Clear StartedAt so the stale-recovery isn't needed for the next attempt.
                tracked.StartedAt = null;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // Fires after a successful scan when AutoFixMode is opted in. Enforces the 24h cooldown
    // internally via IBulkFixApplier (which checks Source.LastBulkApplyAt before opening a PR).
    private async Task MaybeAutoApplyAsync(IServiceScope scope, Source source, CancellationToken ct)
    {
        if (source.AutoFixMode == AutoFixMode.Off)
            return;
        if (source.Type != SourceType.GithubRepo)
            return;

        bool cooldownActive =
            source.AutoFixMode == AutoFixMode.WeeklyDigest
                ? source.LastBulkApplyAt.HasValue
                    && (DateTime.UtcNow - source.LastBulkApplyAt.Value) < TimeSpan.FromDays(7)
                : source.LastBulkApplyAt.HasValue
                    && (DateTime.UtcNow - source.LastBulkApplyAt.Value) < TimeSpan.FromHours(24);

        if (cooldownActive)
            return;

        try
        {
            ShieldDbContext autoDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

            bool hasOpenFindings = await autoDb.Findings.AnyAsync(
                finding => finding.SourceId == source.Id && finding.State == FindingState.Open,
                ct
            );

            if (!hasOpenFindings)
                return;

            List<Finding> openFindings = await autoDb
                .Findings.Where(finding =>
                    finding.SourceId == source.Id && finding.State == FindingState.Open
                )
                .ToListAsync(ct);

            HashSet<Guid> advisoryIds = openFindings
                .Select(finding => finding.AdvisoryRefId)
                .ToHashSet();
            List<Advisory> advisories = await feedsDb
                .Advisories.Where(advisory => advisoryIds.Contains(advisory.Id))
                .ToListAsync(ct);

            IBulkFixApplier applier = scope.ServiceProvider.GetRequiredService<IBulkFixApplier>();
            BulkApplyResult applyResult = await applier.ApplyAllPullRequestAsync(
                source,
                advisories,
                dryRun: false,
                maxPackages: null,
                allowMajorBumps: false,
                ct
            );

            if (applyResult.PullRequestUrl is not null)
            {
                source.LastBulkApplyAt = DateTime.UtcNow;
                source.UpdatedAt = DateTime.UtcNow;
                await autoDb.SaveChangesAsync(ct);

                INotificationPublisher publisher =
                    scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
                await publisher.BroadcastAsync(
                    NotificationKind.SystemMessage,
                    Severity.Low,
                    $"Auto-fix PR opened for {source.Name}",
                    $"{applyResult.Entries.Count} packages bumped — {applyResult.PullRequestUrl}",
                    relatedType: "Source",
                    relatedId: source.Id.ToString(),
                    ct
                );
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "AutoFix failed for source {SourceId}; will retry on next scan",
                source.Id
            );
        }
    }
}
