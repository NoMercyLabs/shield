using Shield.Api.Services;
using Shield.Api.Services.Ecosystems;

namespace Shield.Api.Workers;

// Daily sweep: for every direct InventoryItem on every enabled source, find the newest stable
// version in the PackageMeta cache and upsert a PackageUpdate row when newer-than-current.
// Companion to BulkFixApplier (advisory-driven). Updates feed exists so an operator has one
// place to bump everything across repos — package hygiene, not vulnerability response.
//
// Uses the existing PackageMeta cache rather than hitting npm/Packagist directly: the feed-sync
// pipeline keeps it warm. Source-by-source updates can be made on-demand via the controller
// (POST /api/updates/refresh?sourceId=N) if the cache misses are too sparse for a given repo.
public sealed class UpdateScannerWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UpdateScannerWorker> _log;

    public UpdateScannerWorker(IServiceScopeFactory scopeFactory, ILogger<UpdateScannerWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Update sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IEcosystemRegistry registry =
            scope.ServiceProvider.GetRequiredService<IEcosystemRegistry>();

        // Orphan sweep — drop PackageUpdate rows whose Source no longer exists. Cheap belt-and-
        // braces alongside the controller-side cascade in SourcesController.Delete.
        HashSet<int> liveSourceIds = (
            await db.Sources.Select(source => source.Id).ToListAsync(ct)
        ).ToHashSet();
        List<PackageUpdate> orphans = await db
            .PackageUpdates.Where(update => !liveSourceIds.Contains(update.SourceId))
            .ToListAsync(ct);
        if (orphans.Count > 0)
        {
            db.PackageUpdates.RemoveRange(orphans);
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Update sweep removed {Count} orphan rows", orphans.Count);
        }

        List<Source> sources = await db
            .Sources.Where(source => source.Enabled && source.Type == SourceType.GithubRepo)
            .ToListAsync(ct);

        int sweepUpserts = 0;
        DateTime now = DateTime.UtcNow;
        foreach (Source source in sources)
        {
            ct.ThrowIfCancellationRequested();
            sweepUpserts += await SweepSourceAsync(db, registry, source, now, ct);
        }

        if (sweepUpserts > 0)
            _log.LogInformation("Update sweep upserted {Count} PackageUpdate rows", sweepUpserts);
    }

    public async Task<int> SweepSourceAsync(
        ShieldDbContext db,
        IEcosystemRegistry registry,
        Source source,
        DateTime now,
        CancellationToken ct
    )
    {
        // Latest snapshot's items only — older snapshots are historical and not actionable.
        InventorySnapshot? latestSnapshot = await db
            .InventorySnapshots.Where(snapshot => snapshot.SourceId == source.Id)
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .FirstOrDefaultAsync(ct);
        if (latestSnapshot is null)
            return 0;

        List<InventoryItem> directsRaw = await db
            .InventoryItems.Where(item => item.SnapshotId == latestSnapshot.Id && item.IsDirect)
            .ToListAsync(ct);
        if (directsRaw.Count == 0)
            return 0;

        // Dedupe by (Ecosystem, Name) — monorepos list the same package across multiple
        // manifests, but the PackageUpdates table has UNIQUE(SourceId, Ecosystem, Name), so
        // only one row can exist per package per source. Keep the first occurrence.
        Dictionary<(Ecosystem, string), InventoryItem> directsByKey = new();
        foreach (InventoryItem item in directsRaw)
            directsByKey.TryAdd((item.Ecosystem, item.Name), item);
        List<InventoryItem> directs = [.. directsByKey.Values];

        List<PackageUpdate> existingRows = await db
            .PackageUpdates.Where(update => update.SourceId == source.Id)
            .ToListAsync(ct);
        Dictionary<(Ecosystem, string), PackageUpdate> existingByKey = existingRows.ToDictionary(
            row => (row.Ecosystem, row.Name)
        );

        int upserts = 0;
        foreach (InventoryItem direct in directs)
        {
            ct.ThrowIfCancellationRequested();

            IEcosystem? eco = registry.For(direct.Ecosystem);
            if (eco is null)
                continue;
            LatestPackageInfo? latest;
            try
            {
                latest = await eco.GetLatestStableAsync(direct.Name, ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(
                    ex,
                    "Skipped {Eco}/{Name} on source {SourceId} — registry probe failed",
                    direct.Ecosystem,
                    direct.Name,
                    source.Id
                );
                continue;
            }
            string? latestVersion = latest?.Version;
            DateTime? publishedAt = latest?.PublishedAt;

            if (string.IsNullOrEmpty(latestVersion))
                continue;
            if (!SemVerHelper.IsNewer(direct.Version, latestVersion))
                continue;

            bool isBreakingMajor = SemVerHelper.IsMajorBump(direct.Version, latestVersion);

            if (
                existingByKey.TryGetValue(
                    (direct.Ecosystem, direct.Name),
                    out PackageUpdate? existing
                )
            )
            {
                if (
                    string.Equals(existing.LatestVersion, latestVersion, StringComparison.Ordinal)
                    && string.Equals(
                        existing.CurrentVersion,
                        direct.Version,
                        StringComparison.Ordinal
                    )
                )
                    continue;
                existing.CurrentVersion = direct.Version;
                existing.LatestVersion = latestVersion;
                existing.PublishedAt = publishedAt;
                existing.IsBreakingMajor = isBreakingMajor;
                existing.InventoryItemId = direct.Id;
                existing.DetectedAt = now;
                // Latest version advanced past the previous AppliedAt window — re-open the row.
                existing.AppliedAt = null;
                existing.AppliedPullRequestUrl = null;
                upserts++;
            }
            else
            {
                db.PackageUpdates.Add(
                    new()
                    {
                        SourceId = source.Id,
                        InventoryItemId = direct.Id,
                        Ecosystem = direct.Ecosystem,
                        Name = direct.Name,
                        CurrentVersion = direct.Version,
                        LatestVersion = latestVersion,
                        PublishedAt = publishedAt,
                        IsBreakingMajor = isBreakingMajor,
                        DetectedAt = now,
                    }
                );
                upserts++;
            }
        }

        if (upserts > 0)
            await db.SaveChangesAsync(ct);
        return upserts;
    }
}
