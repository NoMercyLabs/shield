using Microsoft.EntityFrameworkCore;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Matcher;

namespace Shield.Api.Workers;

public sealed class MatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchQueue _matchQueue;
    private readonly IFindingsBroadcaster _broadcaster;
    private readonly ILogger<MatcherWorker> _log;

    public MatcherWorker(
        IServiceScopeFactory scopeFactory,
        MatchQueue matchQueue,
        IFindingsBroadcaster broadcaster,
        ILogger<MatcherWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _matchQueue = matchQueue;
        _broadcaster = broadcaster;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (MatchRequest request in _matchQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Matcher tick failed for request {Request}", request);
            }
        }
    }

    private async Task ProcessAsync(MatchRequest request, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        AdvisoryMatcher matcher = scope.ServiceProvider.GetRequiredService<AdvisoryMatcher>();

        List<InventorySnapshot> snapshots = await LoadSnapshotsAsync(shieldDb, request, ct);
        if (snapshots.Count == 0)
            return;

        List<Advisory> advisories = await feedsDb.Advisories.ToListAsync(ct);
        if (advisories.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        List<Finding> newlyInserted = new();

        foreach (InventorySnapshot snapshot in snapshots)
        {
            List<InventoryItem> items = await shieldDb
                .InventoryItems.Where(item => item.SnapshotId == snapshot.Id)
                .ToListAsync(ct);

            List<Finding> existing = await shieldDb
                .Findings.Where(finding => finding.SourceId == snapshot.SourceId)
                .ToListAsync(ct);

            IReadOnlyList<Finding> matched = matcher.Match(
                snapshot,
                items,
                advisories,
                existing,
                now
            );

            Dictionary<string, Finding> existingByKey = existing.ToDictionary(
                finding => finding.DedupKey,
                finding => finding,
                StringComparer.Ordinal
            );

            foreach (Finding finding in matched)
            {
                if (existingByKey.TryGetValue(finding.DedupKey, out Finding? tracked))
                {
                    tracked.LastSeenAt = now;
                }
                else
                {
                    shieldDb.Findings.Add(finding);
                    existingByKey[finding.DedupKey] = finding;
                    newlyInserted.Add(finding);
                }
            }
        }

        await shieldDb.SaveChangesAsync(ct);

        if (newlyInserted.Count == 0)
            return;

        try
        {
            await _broadcaster.PublishNewAsync(newlyInserted, ct);

            // Same scope — re-tally open counts cheaply now that the insert committed.
            // Piggybacking on the matcher tick is cheaper than a polling watcher: counts
            // only change when findings change, and the matcher is the only path that adds them.
            int low = await shieldDb.Findings.CountAsync(
                finding => finding.State == FindingState.Open && finding.Severity == Severity.Low,
                ct
            );
            int medium = await shieldDb.Findings.CountAsync(
                finding =>
                    finding.State == FindingState.Open && finding.Severity == Severity.Medium,
                ct
            );
            int high = await shieldDb.Findings.CountAsync(
                finding => finding.State == FindingState.Open && finding.Severity == Severity.High,
                ct
            );
            int critical = await shieldDb.Findings.CountAsync(
                finding =>
                    finding.State == FindingState.Open && finding.Severity == Severity.Critical,
                ct
            );
            await _broadcaster.PublishCountsAsync(low, medium, high, critical, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to broadcast {Count} new findings", newlyInserted.Count);
        }
    }

    private static async Task<List<InventorySnapshot>> LoadSnapshotsAsync(
        ShieldDbContext db,
        MatchRequest request,
        CancellationToken ct
    )
    {
        if (request.SnapshotId is Guid snapshotId)
        {
            InventorySnapshot? snapshot = await db.InventorySnapshots.FirstOrDefaultAsync(
                item => item.Id == snapshotId,
                ct
            );
            return snapshot is null
                ? new List<InventorySnapshot>()
                : new List<InventorySnapshot> { snapshot };
        }

        if (request.MatchAll)
        {
            // Re-match latest snapshot per source against the freshly-pulled advisories.
            List<InventorySnapshot> all = await db.InventorySnapshots.ToListAsync(ct);
            return all.GroupBy(item => item.SourceId)
                .Select(group => group.OrderByDescending(item => item.TakenAt).First())
                .ToList();
        }

        return new List<InventorySnapshot>();
    }
}
