using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;

namespace Shield.Matcher;

public sealed class AdvisoryMatcher
{
    private readonly Dictionary<Ecosystem, IVersionComparer> _comparers;
    private readonly ILogger<AdvisoryMatcher> _log;
    private readonly ConcurrentDictionary<Ecosystem, byte> _missingComparerWarned = new();

    public AdvisoryMatcher(IEnumerable<IVersionComparer> comparers)
        : this(comparers, NullLogger<AdvisoryMatcher>.Instance) { }

    public AdvisoryMatcher(IEnumerable<IVersionComparer> comparers, ILogger<AdvisoryMatcher> log)
    {
        ArgumentNullException.ThrowIfNull(comparers);
        ArgumentNullException.ThrowIfNull(log);
        _comparers = comparers.ToDictionary(comparer => comparer.Ecosystem);
        _log = log;
    }

    public IReadOnlyList<Finding> Match(
        InventorySnapshot snapshot,
        IReadOnlyList<InventoryItem> items,
        IReadOnlyList<Advisory> advisories,
        IReadOnlyList<Finding> existingFindings,
        DateTime nowUtc
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(advisories);
        ArgumentNullException.ThrowIfNull(existingFindings);

        Dictionary<string, Finding> existingByKey = existingFindings.ToDictionary(finding =>
            finding.DedupKey
        );
        Dictionary<(Ecosystem, string), List<Advisory>> advisoryIndex = BuildAdvisoryIndex(
            advisories
        );
        List<Finding> results = [];

        foreach (InventoryItem item in items)
        {
            (Ecosystem, string) key = (item.Ecosystem, NormalizeName(item.Name));
            if (!advisoryIndex.TryGetValue(key, out List<Advisory>? candidates))
                continue;

            if (!_comparers.TryGetValue(item.Ecosystem, out IVersionComparer? comparer))
            {
                // No comparer registered for this ecosystem — log once per ecosystem so we
                // notice the blind spot instead of silently skipping advisory matches.
                if (_missingComparerWarned.TryAdd(item.Ecosystem, 0))
                {
                    _log.LogWarning(
                        "No version comparer registered for ecosystem {Ecosystem}; advisories "
                            + "for items in this ecosystem will not match. Register an "
                            + "IVersionComparer with Ecosystem={Ecosystem} via AddShieldMatcher.",
                        item.Ecosystem,
                        item.Ecosystem
                    );
                }
                continue;
            }

            foreach (Advisory advisory in candidates)
            {
                if (!AdvisoryMatchesItem(comparer, item, advisory))
                    continue;

                string dedupKey = DedupKey.Compute(
                    snapshot.SourceId,
                    item.Ecosystem,
                    item.Name,
                    advisory.ExternalId
                );

                if (existingByKey.TryGetValue(dedupKey, out Finding? existing))
                {
                    existing.LastSeenAt = nowUtc;
                    results.Add(existing);
                }
                else
                {
                    // Human-readable summary the alert channels (Discord/Slack/Ntfy/SMTP/Inbox)
                    // prefer over the SHA dedup key. Channels still fall back to DedupKey when
                    // Notes is null (legacy rows from before this lands).
                    string notes = $"{item.Name}@{item.Version} → {advisory.ExternalId}";
                    results.Add(
                        new()
                        {
                            Id = Guid.NewGuid(),
                            SourceId = snapshot.SourceId,
                            InventoryItemId = item.Id,
                            AdvisoryRefId = advisory.Id,
                            Severity = advisory.Severity,
                            FirstSeenAt = nowUtc,
                            LastSeenAt = nowUtc,
                            State = FindingState.Open,
                            DedupKey = dedupKey,
                            Notes = notes,
                        }
                    );
                }
            }
        }

        return results;
    }

    private static Dictionary<(Ecosystem, string), List<Advisory>> BuildAdvisoryIndex(
        IReadOnlyList<Advisory> advisories
    )
    {
        Dictionary<(Ecosystem, string), List<Advisory>> index = new();
        foreach (Advisory advisory in advisories)
        {
            (Ecosystem, string) key = (advisory.Ecosystem, NormalizeName(advisory.PackageName));
            if (!index.TryGetValue(key, out List<Advisory>? bucket))
            {
                bucket = [];
                index[key] = bucket;
            }
            bucket.Add(advisory);
        }
        return index;
    }

    private static bool AdvisoryMatchesItem(
        IVersionComparer comparer,
        InventoryItem item,
        Advisory advisory
    )
    {
        IReadOnlyList<VersionRange> ranges = ParseRanges(advisory.AffectedRangesJson);
        foreach (VersionRange range in ranges)
        {
            if (comparer.Satisfies(item.Version, range))
                return true;
        }
        return false;
    }

    private static IReadOnlyList<VersionRange> ParseRanges(string affectedRangesJson)
    {
        if (string.IsNullOrWhiteSpace(affectedRangesJson))
            return [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(affectedRangesJson);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return [];

            List<VersionRange> ranges = [];
            foreach (JsonElement element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                if (element.TryGetProperty("events", out JsonElement events))
                {
                    foreach (VersionRange range in VersionRange.ParseOsvEvents(events))
                        ranges.Add(range);
                }
                else if (
                    element.ValueKind == JsonValueKind.Object
                    && (
                        element.TryGetProperty("introduced", out _)
                        || element.TryGetProperty("fixed", out _)
                        || element.TryGetProperty("last_affected", out _)
                    )
                )
                {
                    foreach (VersionRange range in VersionRange.ParseOsvEvents(root))
                        ranges.Add(range);
                    return ranges;
                }
            }
            return ranges;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();
}
