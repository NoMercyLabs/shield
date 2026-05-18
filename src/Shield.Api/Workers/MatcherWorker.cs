using System.Text.Json;
using Shield.Api.Workers.Queues;
using Shield.Core.Results;
using Shield.Feeds.Osv;
using Shield.Feeds.Osv.Models;
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

        // Pull every inventory row touched by this match pass up front so we can both build
        // the OSV query set and run the in-memory matcher off the same data.
        HashSet<Guid> snapshotIds = snapshots.Select(snapshot => snapshot.Id).ToHashSet();
        List<InventoryItem> allItems = await shieldDb
            .InventoryItems.Where(item => snapshotIds.Contains(item.SnapshotId))
            .ToListAsync(ct);

        // OSV is package-keyed. KEV writes Ecosystem.Os rows that never line up with our
        // package-ecosystem inventory, and GHSA is rate-limited today — so without an OSV
        // round trip the Advisories table can't produce a single match. Drive the per-package
        // query path from the actual inventory so the matcher has something to chew on.
        OsvFeedSync osv = scope.ServiceProvider.GetRequiredService<OsvFeedSync>();
        FeedSyncState osvState =
            await feedsDb.FeedSyncStates.FirstOrDefaultAsync(item => item.Feed == Feed.Osv, ct)
            ?? new FeedSyncState
            {
                Id = Guid.NewGuid(),
                Feed = Feed.Osv,
                NextRunAt = DateTime.UtcNow,
            };

        List<OsvQuery> queries = BuildOsvQueries(allItems);
        if (queries.Count > 0)
        {
            HashSet<string> knownIds = await feedsDb
                .Advisories.Where(advisory => advisory.Feed == Feed.Osv)
                .Select(advisory => advisory.ExternalId)
                .ToListAsync(ct)
                .ContinueWith(task => task.Result.ToHashSet(StringComparer.Ordinal), ct);

            _log.LogInformation(
                "OSV query: {Queries} package(s) across {Snapshots} snapshot(s); {KnownIds} cached IDs",
                queries.Count,
                snapshots.Count,
                knownIds.Count
            );

            (IReadOnlyList<Advisory> fetched, FeedSyncResult osvResult) = await osv.QueryBatchAsync(
                osvState,
                queries,
                knownIds,
                ct
            );

            if (!osvResult.Success)
            {
                _log.LogWarning(
                    "OSV query failed during matcher pass: {Error}",
                    osvResult.Error ?? "unknown"
                );
            }
            else
            {
                await UpsertAdvisoriesAsync(feedsDb, fetched, ct);

                // Advance the OSV cursor so the scheduler-side path (when it's implemented in
                // Phase 2) doesn't re-walk material we already pulled.
                osvState.LastSuccessAt = DateTime.UtcNow;
                osvState.LastError = null;
                osvState.Cursor = osvResult.NextCursor ?? osvState.Cursor;
                bool isTracked = feedsDb.FeedSyncStates.Local.Contains(osvState);
                if (!isTracked && await feedsDb.FeedSyncStates.FindAsync([osvState.Id], ct) is null)
                    feedsDb.FeedSyncStates.Add(osvState);
                await feedsDb.SaveChangesAsync(ct);
            }
        }

        List<Advisory> advisories = await feedsDb.Advisories.ToListAsync(ct);
        if (advisories.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        List<Finding> newlyInserted = [];
        List<(Finding Finding, FindingState PreviousState)> autoResolved = [];

        foreach (InventorySnapshot snapshot in snapshots)
        {
            List<InventoryItem> items = allItems
                .Where(item => item.SnapshotId == snapshot.Id)
                .ToList();

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

            // Auto-resolve pass: any Open/Acked finding whose dedup key is no longer in
            // the matched set has been fixed. Guard: only auto-resolve if the package is
            // still present in the snapshot (meaning we observed it and it didn't match)
            // OR if the package was removed from the manifest entirely — both are real fixes.
            // If an advisory was retracted (not in the advisory set) we don't want to close
            // findings, but that case is indistinguishable from "package fixed" here, so the
            // guard is on whether the advisory's referenced package still appears in the items.
            HashSet<string> matchedKeys = matched
                .Select(finding => finding.DedupKey)
                .ToHashSet(StringComparer.Ordinal);

            // Build (ecosystem, packageName) pairs that appear in this snapshot's items.
            HashSet<(Ecosystem, string)> presentPackages = items
                .Select(inventoryItem => (inventoryItem.Ecosystem, inventoryItem.Name))
                .ToHashSet();

            foreach (Finding existingFinding in existing)
            {
                if (
                    existingFinding.State != FindingState.Open
                    && existingFinding.State != FindingState.Acked
                )
                    continue;
                if (matchedKeys.Contains(existingFinding.DedupKey))
                    continue;

                // Determine whether the package for this finding is still in the snapshot.
                // If the advisory for this finding no longer has a matching package row in
                // the advisory table, the advisory was retracted — skip auto-resolve.
                Advisory? referencedAdvisory = advisories.FirstOrDefault(adv =>
                    adv.Id == existingFinding.AdvisoryRefId
                );
                if (referencedAdvisory is null)
                    continue;

                // Package is present in snapshot but didn't match (version bumped) OR package
                // is gone entirely — both are genuine resolutions. The advisory-retracted case
                // is handled above by the `referencedAdvisory is null` guard.
                FindingState previousState = existingFinding.State;
                existingFinding.State = FindingState.AutoResolved;
                existingFinding.LastSeenAt = now;
                autoResolved.Add((existingFinding, previousState));
            }
        }

        // Batch-write audit entries for all auto-resolved findings in one SaveChangesAsync.
        foreach ((Finding resolvedFinding, FindingState previousState) in autoResolved)
        {
            shieldDb.AuditEntries.Add(
                new AuditEntry
                {
                    Id = Guid.NewGuid(),
                    At = now,
                    ActorName = "system",
                    Action = "finding.auto_resolved",
                    TargetType = "Finding",
                    TargetId = resolvedFinding.Id.ToString(),
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            dedupKey = resolvedFinding.DedupKey,
                            previousState = previousState.ToString(),
                        }
                    ),
                }
            );
        }

        await shieldDb.SaveChangesAsync(ct);

        if (autoResolved.Count > 0)
        {
            _log.LogInformation(
                "Auto-resolved {Count} finding(s) whose packages are no longer in the vulnerable range",
                autoResolved.Count
            );
        }

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

    private static List<OsvQuery> BuildOsvQueries(IReadOnlyList<InventoryItem> items)
    {
        HashSet<(Ecosystem, string, string)> seen = [];
        List<OsvQuery> queries = [];
        foreach (InventoryItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Version))
                continue;
            (Ecosystem, string, string) key = (item.Ecosystem, item.Name, item.Version);
            if (!seen.Add(key))
                continue;
            queries.Add(new(item.Ecosystem, item.Name, item.Version));
        }
        return queries;
    }

    // Upsert by (Feed, ExternalId, PackageName, Ecosystem) so the same OSV vuln that fans out
    // to several affected packages keeps each variant row, while replays update the existing
    // row instead of duplicating it. Inline here to avoid the captive-dep dance with
    // GhsaFeedSync's Singleton IAdvisorySink registration.
    private static async Task UpsertAdvisoriesAsync(
        FeedsDbContext db,
        IReadOnlyList<Advisory> advisories,
        CancellationToken ct
    )
    {
        if (advisories.Count == 0)
            return;

        Dictionary<(string, string, Ecosystem), Advisory> incomingByKey = new();
        foreach (Advisory advisory in advisories)
        {
            (string, string, Ecosystem) key = (
                advisory.ExternalId,
                advisory.PackageName,
                advisory.Ecosystem
            );
            incomingByKey[key] = advisory;
        }

        HashSet<string> incomingIds = incomingByKey
            .Values.Select(advisory => advisory.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        List<Advisory> existingRows = await db
            .Advisories.Where(advisory =>
                advisory.Feed == Feed.Osv && incomingIds.Contains(advisory.ExternalId)
            )
            .ToListAsync(ct);

        Dictionary<(string, string, Ecosystem), Advisory> existingByKey = existingRows
            .GroupBy(advisory => (advisory.ExternalId, advisory.PackageName, advisory.Ecosystem))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (KeyValuePair<(string, string, Ecosystem), Advisory> entry in incomingByKey)
        {
            Advisory incoming = entry.Value;
            if (existingByKey.TryGetValue(entry.Key, out Advisory? row))
            {
                row.AffectedRangesJson = incoming.AffectedRangesJson;
                row.Severity = incoming.Severity;
                row.Cvss = incoming.Cvss;
                row.Summary = incoming.Summary;
                row.ReferencesJson = incoming.ReferencesJson;
                row.PublishedAt = incoming.PublishedAt;
                row.ModifiedAt = incoming.ModifiedAt;
                row.FetchedAt = incoming.FetchedAt;
            }
            else
            {
                if (incoming.Id == Guid.Empty)
                    incoming.Id = Guid.NewGuid();
                db.Advisories.Add(incoming);
            }
        }

        await db.SaveChangesAsync(ct);
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
            return snapshot is null ? [] : [snapshot];
        }

        if (request.MatchAll)
        {
            // Re-match latest snapshot per source against the freshly-pulled advisories.
            List<InventorySnapshot> all = await db.InventorySnapshots.ToListAsync(ct);
            return all.GroupBy(item => item.SourceId)
                .Select(group => group.OrderByDescending(item => item.TakenAt).First())
                .ToList();
        }

        return [];
    }
}
