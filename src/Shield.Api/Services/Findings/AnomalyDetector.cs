using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Contracts;
using Shield.Api.Workers;
using Shield.Api.Workers.Queues;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services.Findings;

public sealed class AnomalyDetector : IAnomalyDetector
{
    // BrandNew window — 30 days mirrors the maintainer-drift detector's "fresh
    // surface" intuition without being so long that benign new releases get
    // permanently flagged.
    private static readonly TimeSpan BrandNewWindow = TimeSpan.FromDays(30);

    private readonly ShieldDbContext _shieldDb;
    private readonly FeedsDbContext _feedsDb;
    private readonly MatchQueue _matchQueue;
    private readonly TimeProvider _time;
    private readonly Ecosystems.IEcosystemRegistry _ecosystems;
    private readonly ITyposquatDetector _typosquat;
    private readonly ILogger<AnomalyDetector> _log;
    private readonly INotificationPublisher? _notifications;

    public AnomalyDetector(
        ShieldDbContext shieldDb,
        FeedsDbContext feedsDb,
        MatchQueue matchQueue,
        TimeProvider time,
        Ecosystems.IEcosystemRegistry ecosystems,
        ITyposquatDetector typosquat,
        ILogger<AnomalyDetector> log,
        INotificationPublisher? notifications = null
    )
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
        _matchQueue = matchQueue;
        _time = time;
        _ecosystems = ecosystems;
        _typosquat = typosquat;
        _log = log;
        _notifications = notifications;
    }

    public async Task<int> AnalyzeNewSnapshotAsync(
        int sourceId,
        Guid newSnapshotId,
        CancellationToken ct
    )
    {
        InventorySnapshot? newer = await _shieldDb
            .InventorySnapshots.AsNoTracking()
            .FirstOrDefaultAsync(snapshot => snapshot.Id == newSnapshotId, ct);
        if (newer is null)
            return 0;

        InventorySnapshot? older = await _shieldDb
            .InventorySnapshots.AsNoTracking()
            .Where(snapshot => snapshot.SourceId == sourceId && snapshot.TakenAt < newer.TakenAt)
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .FirstOrDefaultAsync(ct);

        // First scan — every item is "added" but none are anomalies. Bail out
        // before we spam findings on legitimate baselines.
        if (older is null)
            return 0;

        List<InventoryItem> newerItems = await _shieldDb
            .InventoryItems.AsNoTracking()
            .Where(item => item.SnapshotId == newSnapshotId)
            .ToListAsync(ct);

        HashSet<(Ecosystem, string)> olderKeys = (
            await _shieldDb
                .InventoryItems.AsNoTracking()
                .Where(item => item.SnapshotId == older.Id)
                .Select(item => new { item.Ecosystem, item.Name })
                .ToListAsync(ct)
        )
            .Select(row => (row.Ecosystem, NormalizeName(row.Name)))
            .ToHashSet();

        List<InventoryItem> added = newerItems
            .Where(item => !olderKeys.Contains((item.Ecosystem, NormalizeName(item.Name))))
            .ToList();
        if (added.Count == 0)
            return 0;

        DateTime nowUtc = _time.GetUtcNow().UtcDateTime;
        int synthesised = 0;
        bool wroteAdvisory = false;

        foreach (InventoryItem item in added)
        {
            PackageMeta? current = await LoadMetaAsync(item.Ecosystem, item.Name, item.Version, ct);
            PackageMeta? prior = await LoadPriorMetaAsync(
                item.Ecosystem,
                item.Name,
                item.Version,
                ct
            );

            AnomalyFlags flags = Evaluate(
                item.Ecosystem,
                item.Name,
                item.Version,
                current,
                prior,
                nowUtc
            );

            // Only persist findings for the high-signal flags. SingleMaintainer +
            // Deprecated are surfaced in the UI diff but don't deserve a finding
            // on their own — too noisy.
            AnomalyFlags persistable =
                flags
                & (
                    AnomalyFlags.BrandNew
                    | AnomalyFlags.NewMaintainerThisVersion
                    | AnomalyFlags.Typosquat
                );
            if (persistable == AnomalyFlags.None)
                continue;

            foreach (AnomalyFlags single in EnumerateFlags(persistable))
            {
                Advisory advisory = BuildSyntheticAdvisory(item, single, nowUtc);
                _feedsDb.Advisories.Add(advisory);
                synthesised++;
                wroteAdvisory = true;
            }
        }

        if (!wroteAdvisory)
            return 0;

        await _feedsDb.SaveChangesAsync(ct);

        if (_notifications is not null)
        {
            try
            {
                await _notifications.BroadcastAsync(
                    NotificationKind.NewAnomaly,
                    Severity.Medium,
                    $"{synthesised} supply-chain anomal{(synthesised == 1 ? "y" : "ies")} detected",
                    $"Source #{sourceId} inventory diff produced {synthesised} synthetic advisor"
                        + $"{(synthesised == 1 ? "y" : "ies")} (typosquat / brand-new / new maintainer).",
                    relatedType: "Source",
                    relatedId: sourceId.ToString(),
                    ct
                );
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to publish anomaly notification");
            }
        }

        try
        {
            await _matchQueue.EnqueueAsync(new(newSnapshotId, sourceId, MatchAll: false), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to enqueue matcher after anomaly detection");
        }

        return synthesised;
    }

    public AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc
    )
    {
        AnomalyFlags flags = AnomalyFlags.None;

        if (current is not null)
        {
            if (current.PublishedAt is { } publishedAt && publishedAt >= nowUtc - BrandNewWindow)
                flags |= AnomalyFlags.BrandNew;

            if (current.Deprecated)
                flags |= AnomalyFlags.Deprecated;

            IReadOnlyList<string> currentMaintainers = ParseMaintainers(current.MaintainersJson);
            if (currentMaintainers.Count == 1)
                flags |= AnomalyFlags.SingleMaintainer;

            if (priorVersionMeta is not null)
            {
                IReadOnlyList<string> priorMaintainers = ParseMaintainers(
                    priorVersionMeta.MaintainersJson
                );
                bool changed = !currentMaintainers
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(
                        priorMaintainers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase
                    );
                if (changed)
                    flags |= AnomalyFlags.NewMaintainerThisVersion;
            }
        }

        if (_typosquat.IsTyposquat(ecosystem, name))
            flags |= AnomalyFlags.Typosquat;

        if (ecosystem == Ecosystem.Npm && _typosquat.IsScopeMismatch(name))
            flags |= AnomalyFlags.HighScopeMismatch;

        return flags;
    }

    private async Task<PackageMeta?> LoadMetaAsync(
        Ecosystem ecosystem,
        string name,
        string version,
        CancellationToken ct
    ) =>
        await _feedsDb
            .PackageMetas.AsNoTracking()
            .FirstOrDefaultAsync(
                meta => meta.Ecosystem == ecosystem && meta.Name == name && meta.Version == version,
                ct
            );

    private async Task<PackageMeta?> LoadPriorMetaAsync(
        Ecosystem ecosystem,
        string name,
        string currentVersion,
        CancellationToken ct
    )
    {
        // No semver ordering here — npm registry feed only stores discrete versions
        // we've fetched. Best heuristic: newest fetched record for the same package
        // that isn't the current one.
        return await _feedsDb
            .PackageMetas.AsNoTracking()
            .Where(meta =>
                meta.Ecosystem == ecosystem && meta.Name == name && meta.Version != currentVersion
            )
            .OrderByDescending(meta => meta.PublishedAt ?? meta.FetchedAt)
            .FirstOrDefaultAsync(ct);
    }

    private static Advisory BuildSyntheticAdvisory(
        InventoryItem item,
        AnomalyFlags flag,
        DateTime nowUtc
    )
    {
        Severity severity = flag switch
        {
            AnomalyFlags.Typosquat => Severity.High,
            AnomalyFlags.NewMaintainerThisVersion => Severity.Medium,
            _ => Severity.Low,
        };

        string version = item.Version;
        // Narrow exact-version range — match only the version we just saw added.
        // "{version}-shield-anomaly+1" sorts strictly above {version} under semver
        // and is the exact format the brief requires.
        string affectedRanges =
            $"[{{\"events\":[{{\"introduced\":\"{Escape(version)}\"}},{{\"fixed\":\"{Escape(version)}-shield-anomaly+1\"}}]}}]";

        return new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.NpmRegistry,
            ExternalId = $"anomaly:{item.Name}:{version}:{flag}:{nowUtc:yyyyMMddHHmmss}",
            Ecosystem = item.Ecosystem,
            PackageName = item.Name,
            AffectedRangesJson = affectedRanges,
            Severity = severity,
            Summary = $"Supply-chain anomaly: {flag} for {item.Name}@{version}",
            ReferencesJson = "[]",
            PublishedAt = nowUtc,
            ModifiedAt = nowUtc,
            FetchedAt = nowUtc,
        };
    }

    private static IEnumerable<AnomalyFlags> EnumerateFlags(AnomalyFlags flags)
    {
        if (flags.HasFlag(AnomalyFlags.Typosquat))
            yield return AnomalyFlags.Typosquat;
        if (flags.HasFlag(AnomalyFlags.NewMaintainerThisVersion))
            yield return AnomalyFlags.NewMaintainerThisVersion;
        if (flags.HasFlag(AnomalyFlags.BrandNew))
            yield return AnomalyFlags.BrandNew;
    }

    private static IReadOnlyList<string> ParseMaintainers(string maintainersJson)
    {
        if (string.IsNullOrWhiteSpace(maintainersJson))
            return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(maintainersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            List<string> maintainers = [];
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    string? value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        maintainers.Add(value);
                }
                else if (
                    element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("name", out JsonElement nameEl)
                    && nameEl.ValueKind == JsonValueKind.String
                )
                {
                    string? value = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        maintainers.Add(value);
                }
            }
            return maintainers;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
