using System.Globalization;
using System.Text.Json;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Services.Findings;

public sealed class AnomalyDetector : IAnomalyDetector
{
    // BrandNew window — anything PublishedAt within this window is "fresh surface" enough
    // to be one of the supply-chain signals.
    private static readonly TimeSpan BrandNewWindow = TimeSpan.FromDays(30);

    // Typosquat-candidate freshness — broader than BrandNew because typosquats often
    // sit on the registry for weeks before being depended on by a victim.
    private static readonly TimeSpan TyposquatFreshWindow = TimeSpan.FromDays(60);

    // Anything at-or-above this weekly download count is "popular" — both as a name
    // candidates can typosquat, and as a self-defence: if the package the user just
    // installed has 100k/week, it isn't a typosquat regardless of name similarity. The
    // threshold is deliberately high — `y18n`-class false positives only fire when the
    // BENIGN package gets misclassified as a typosquat, so the bar to claim popularity
    // is the bar that prevents the false positive.
    private const long PopularWeeklyDownloads = 100_000;

    // A candidate with very few downloads + a name similar to a popular package + recent
    // publish date is the textbook typosquat profile. The download bar is conservative;
    // most real attacks ship with < 100 downloads/week.
    private const long SuspiciouslyLowWeeklyDownloads = 1_000;

    // Levenshtein cap — distance 1 covers single-letter swaps (reqests vs requests),
    // distance 2 covers most squat patterns including digit-for-letter substitutions.
    // Going to 3 admits too many legitimate two-edit collisions.
    private const int TyposquatDistanceCap = 2;

    // Cap input length for Levenshtein — anything longer than 64 chars is almost
    // certainly not a typosquat candidate (real squats target short, memorable names).
    private const int MaxNameLengthForLevenshtein = 64;

    private readonly ShieldDbContext _shieldDb;
    private readonly FeedsDbContext _feedsDb;
    private readonly MatchQueue _matchQueue;
    private readonly TimeProvider _time;
    private readonly Ecosystems.IEcosystemRegistry _ecosystems;
    private readonly ILogger<AnomalyDetector> _log;
    private readonly INotificationPublisher? _notifications;

    public AnomalyDetector(
        ShieldDbContext shieldDb,
        FeedsDbContext feedsDb,
        MatchQueue matchQueue,
        TimeProvider time,
        Ecosystems.IEcosystemRegistry ecosystems,
        ILogger<AnomalyDetector> log,
        INotificationPublisher? notifications = null
    )
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
        _matchQueue = matchQueue;
        _time = time;
        _ecosystems = ecosystems;
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

        // First scan — every item is "added" but none are anomalies. Bail out before we
        // spam findings on legitimate baselines.
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

        // Load the popular-names set once per ecosystem represented in the diff. The
        // detector calls Evaluate for every added item, and the popular set is identical
        // for all items sharing an ecosystem.
        HashSet<Ecosystem> ecosystems = added.Select(item => item.Ecosystem).ToHashSet();
        Dictionary<Ecosystem, IReadOnlySet<string>> popularByEcosystem = new();
        foreach (Ecosystem ecosystem in ecosystems)
            popularByEcosystem[ecosystem] = await LoadPopularNamesAsync(ecosystem, ct);

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
                nowUtc,
                popularByEcosystem[item.Ecosystem]
            );

            // Only persist findings for the high-signal flags. SingleMaintainer + Deprecated
            // are surfaced in the UI diff but don't deserve a finding on their own — too noisy.
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
                    relatedId: sourceId.ToString(CultureInfo.InvariantCulture),
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

    // Pure-function evaluation surface (used by both the live detector and the diff
    // endpoint). The popular-names set is a parameter so callers can amortise the lookup
    // across many items in one ecosystem.
    public AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc,
        IReadOnlySet<string> popularNamesInEcosystem
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

        if (IsTyposquat(ecosystem, name, current, popularNamesInEcosystem, nowUtc))
            flags |= AnomalyFlags.Typosquat;

        if (ecosystem == Ecosystem.Npm && IsScopeMismatch(name, popularNamesInEcosystem))
            flags |= AnomalyFlags.HighScopeMismatch;

        return flags;
    }

    // Legacy overload — supplies an empty popular-names set so any caller that hasn't
    // been updated yet keeps building. The non-empty path is the one anomaly detection
    // depends on; an empty set just means no typosquat flag fires, which is strictly
    // safer than the old hardcoded-list false-positive behaviour.
    public AnomalyFlags Evaluate(
        Ecosystem ecosystem,
        string name,
        string version,
        PackageMeta? current,
        PackageMeta? priorVersionMeta,
        DateTime nowUtc
    ) => Evaluate(ecosystem, name, version, current, priorVersionMeta, nowUtc, EmptyPopularNames);

    private static readonly IReadOnlySet<string> EmptyPopularNames = new HashSet<string>();

    // Multi-signal typosquat. Fires only when:
    //   - the candidate's name is Levenshtein-close to a popular package, AND
    //   - the candidate is NOT itself popular (defends against the y18n-class false
    //     positive where a legit 100M-downloads-per-week package was 2 edits from a
    //     popular name and the old detector flagged it), AND
    //   - the candidate looks like a real squat: low confirmed download count OR
    //     (brand-new AND single-maintainer).
    private static bool IsTyposquat(
        Ecosystem ecosystem,
        string name,
        PackageMeta? candidateMeta,
        IReadOnlySet<string> popularNames,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLengthForLevenshtein)
            return false;
        if (popularNames.Count == 0)
            return false;

        // Self-popularity check: skip if the candidate itself is in the popular set OR
        // its own download count crosses the popularity threshold. Either is sufficient
        // proof of legitimacy.
        if (popularNames.Contains(name) || candidateMeta?.WeeklyDownloads >= PopularWeeklyDownloads)
            return false;

        // Name-similarity gate.
        bool nameLooksLikePopular = false;
        foreach (string popular in popularNames)
        {
            if (popular.Length > MaxNameLengthForLevenshtein)
                continue;
            int lengthDelta = Math.Abs(popular.Length - name.Length);
            if (lengthDelta > TyposquatDistanceCap)
                continue;
            int distance = Levenshtein(popular, name, TyposquatDistanceCap);
            if (distance is > 0 and <= TyposquatDistanceCap)
            {
                nameLooksLikePopular = true;
                break;
            }
        }
        if (!nameLooksLikePopular)
            return false;

        // Without registry metadata we can't tell legit-but-unknown apart from squat.
        // Refuse to fire — the registry sync will catch up and a follow-up scan will
        // re-evaluate with real signals.
        if (candidateMeta is null)
            return false;

        bool isLowTraffic =
            candidateMeta.WeeklyDownloads is not null
            && candidateMeta.WeeklyDownloads < SuspiciouslyLowWeeklyDownloads;

        bool isBrandNew =
            candidateMeta.PublishedAt is { } publishedAt
            && (nowUtc - publishedAt) <= TyposquatFreshWindow;

        bool isSingleMaintainer = ParseMaintainers(candidateMeta.MaintainersJson).Count == 1;

        return isLowTraffic || (isBrandNew && isSingleMaintainer);
    }

    private static bool IsScopeMismatch(string name, IReadOnlySet<string> popularNames)
    {
        // Match shapes like "@scope/inner". The classic confusion attack is @lodash/lodash
        // where the scope advertises lodash but the inner name is also lodash — i.e. the
        // package is pretending to be lodash inside a scope the real lodash team doesn't
        // own.
        if (!name.StartsWith('@'))
            return false;
        int slash = name.IndexOf('/');
        if (slash <= 1 || slash == name.Length - 1)
            return false;
        string scope = name.Substring(1, slash - 1);
        string inner = name[(slash + 1)..];
        if (string.Equals(scope, inner, StringComparison.OrdinalIgnoreCase))
            return popularNames.Contains(inner);
        return false;
    }

    private async Task<IReadOnlySet<string>> LoadPopularNamesAsync(
        Ecosystem ecosystem,
        CancellationToken ct
    )
    {
        // Distinct package names whose WeeklyDownloads cross the popularity floor in this
        // ecosystem. Driven entirely by registry-sync data — no hardcoded curation. If the
        // registry feed for this ecosystem hasn't run, returns an empty set and the
        // typosquat path no-ops (preferred over false positives).
        List<string> names = await _feedsDb
            .PackageMetas.AsNoTracking()
            .Where(meta =>
                meta.Ecosystem == ecosystem
                && meta.WeeklyDownloads != null
                && meta.WeeklyDownloads >= PopularWeeklyDownloads
            )
            .Select(meta => meta.Name)
            .Distinct()
            .ToListAsync(ct);
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
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
        // No semver ordering here — registry feed only stores discrete versions we've fetched.
        // Best heuristic: newest fetched record for the same package that isn't the current one.
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
        // "{version}-shield-anomaly+1" sorts strictly above {version} under semver and is
        // the exact format the brief requires.
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

    // Iterative Levenshtein with an early-exit cap. Returns int.MaxValue if the distance
    // is known to exceed `cap` — caller treats that as "too far".
    private static int Levenshtein(string a, string b, int cap)
    {
        int lenA = a.Length;
        int lenB = b.Length;
        if (lenA == 0)
            return lenB;
        if (lenB == 0)
            return lenA;

        int[] prev = new int[lenB + 1];
        int[] curr = new int[lenB + 1];
        for (int column = 0; column <= lenB; column++)
            prev[column] = column;

        for (int row = 1; row <= lenA; row++)
        {
            curr[0] = row;
            int minInRow = curr[0];
            for (int column = 1; column <= lenB; column++)
            {
                int cost =
                    char.ToLowerInvariant(a[row - 1]) == char.ToLowerInvariant(b[column - 1])
                        ? 0
                        : 1;
                curr[column] = Math.Min(
                    Math.Min(curr[column - 1] + 1, prev[column] + 1),
                    prev[column - 1] + cost
                );
                if (curr[column] < minInRow)
                    minInRow = curr[column];
            }
            if (minInRow > cap)
                return int.MaxValue;

            (prev, curr) = (curr, prev);
        }
        return prev[lenB];
    }
}
