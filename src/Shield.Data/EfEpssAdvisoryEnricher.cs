using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

/// Streams EPSS score/percentile updates onto existing Advisory rows. EPSS covers every CVE,
/// so this NEVER inserts thin advisories — only enriches rows that already exist. The caller
/// pumps batches (~500 entries) and we apply them per batch to keep the change tracker bounded.
public sealed class EfEpssAdvisoryEnricher : IEpssAdvisoryEnricher
{
    private readonly FeedsDbContext _db;

    public EfEpssAdvisoryEnricher(FeedsDbContext db)
    {
        _db = db;
    }

    public async ValueTask<int> ApplyBatchAsync(
        IReadOnlyList<EpssEntry> batch,
        CancellationToken ct
    )
    {
        if (batch.Count == 0)
            return 0;

        Dictionary<string, EpssEntry> byCve = batch
            .GroupBy(entry => entry.CveId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );

        HashSet<string> cveIds = byCve.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cveIds.Count == 0)
            return 0;

        // Match by ExternalId equality only — ReferencesJson scanning would balloon for 250k
        // EPSS rows. OSV/GHSA both store CVEs as ExternalId for most advisories anyway.
        List<Advisory> matches = await _db
            .Advisories.Where(advisory => cveIds.Contains(advisory.ExternalId))
            .ToListAsync(ct);

        int updated = 0;
        foreach (Advisory advisory in matches)
        {
            if (!byCve.TryGetValue(advisory.ExternalId, out EpssEntry? entry))
                continue;
            advisory.EpssScore = entry.Score;
            advisory.EpssPercentile = entry.Percentile;
            updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync(ct);

        // Clear the change tracker so subsequent batches don't drag the previous batch's
        // tracked entities along — keeps memory bounded across the streaming EPSS sync.
        _db.ChangeTracker.Clear();
        return updated;
    }
}
