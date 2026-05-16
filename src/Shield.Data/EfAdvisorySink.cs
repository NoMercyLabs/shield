using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

/// Upserts advisories into <see cref="FeedsDbContext.Advisories"/>, keyed by (Feed, ExternalId).
public sealed class EfAdvisorySink : IAdvisorySink
{
    private readonly FeedsDbContext _db;

    public EfAdvisorySink(FeedsDbContext db)
    {
        _db = db;
    }

    public async ValueTask UpsertAsync(IReadOnlyList<Advisory> advisories, CancellationToken ct)
    {
        if (advisories.Count == 0)
            return;

        // Same OSV vuln can map to multiple affected packages — each emitted as a separate
        // Advisory with identical (Feed, ExternalId, PackageName, Ecosystem). Dedupe on the full
        // logical key so the unique index (Feed, ExternalId) is preserved while still capturing
        // every (package, ecosystem) variant the feed reported.
        foreach (IGrouping<Feed, Advisory> group in advisories.GroupBy(advisory => advisory.Feed))
        {
            Feed feed = group.Key;
            Dictionary<
                (string ExternalId, string PackageName, Ecosystem Ecosystem),
                Advisory
            > incomingByKey = new();
            foreach (Advisory advisory in group)
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

            List<Advisory> existingRows = await _db
                .Advisories.Where(advisory =>
                    advisory.Feed == feed && incomingIds.Contains(advisory.ExternalId)
                )
                .ToListAsync(ct);

            Dictionary<(string, string, Ecosystem), Advisory> existingByKey = existingRows
                .GroupBy(advisory =>
                    (advisory.ExternalId, advisory.PackageName, advisory.Ecosystem)
                )
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
                    _db.Advisories.Add(incoming);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
