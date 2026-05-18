using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

/// Applies CISA KEV catalog entries to the Advisories table. Sets IsKev / KevAddedAt /
/// KevDueDate on every Advisory whose ExternalId equals the CVE id OR whose ReferencesJson
/// contains the id. For unmatched CVEs, persists a thin KEV-only Advisory so the dashboard
/// still surfaces it before OSV/GHSA pick it up.
public sealed class EfKevAdvisoryEnricher : IKevAdvisoryEnricher
{
    private readonly FeedsDbContext _db;

    public EfKevAdvisoryEnricher(FeedsDbContext db)
    {
        _db = db;
    }

    public async ValueTask<KevEnrichmentResult> ApplyAsync(
        IReadOnlyList<KevCatalogEntry> entries,
        CancellationToken ct
    )
    {
        if (entries.Count == 0)
            return new(0, 0);

        HashSet<string> cveIds = entries
            .Select(entry => entry.CveId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (cveIds.Count == 0)
            return new(0, 0);

        Dictionary<string, KevCatalogEntry> byCve = entries
            .GroupBy(entry => entry.CveId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );

        // Match advisories whose ExternalId IS the CVE id directly. ReferencesJson LIKE match
        // would scan-everything on SQLite — keep it simple: pull rows already linked by id, plus
        // a second pass over rows whose ReferencesJson mentions the id (small candidate set
        // because we filter by the same id list).
        List<Advisory> byExternalId = await _db
            .Advisories.Where(advisory => cveIds.Contains(advisory.ExternalId))
            .ToListAsync(ct);

        HashSet<Guid> alreadyTouched = byExternalId.Select(advisory => advisory.Id).ToHashSet();

        // Second pass — scan advisories that mention any KEV CVE in ReferencesJson. SQLite LIKE
        // is fine here because the candidate set is tiny (~1000 CVEs) and ReferencesJson rarely
        // exceeds a few hundred bytes per row. We pull the full table once if we have to but
        // narrow via OR of LIKEs per cve so SQLite can short-circuit on early matches.
        List<Advisory> byReference = [];
        foreach (string cveId in cveIds)
        {
            string needle = $"%{cveId}%";
            List<Advisory> matches = await _db
                .Advisories.Where(advisory =>
                    !alreadyTouched.Contains(advisory.Id)
                    && EF.Functions.Like(advisory.ReferencesJson, needle)
                )
                .ToListAsync(ct);
            foreach (Advisory advisory in matches)
            {
                if (alreadyTouched.Add(advisory.Id))
                    byReference.Add(advisory);
            }
        }

        int updated = 0;
        HashSet<string> mappedCveIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (Advisory advisory in byExternalId.Concat(byReference))
        {
            // Match the entry by external id when possible, otherwise scan references for the cve.
            KevCatalogEntry? entry = null;
            if (byCve.TryGetValue(advisory.ExternalId, out KevCatalogEntry? direct))
            {
                entry = direct;
                mappedCveIds.Add(advisory.ExternalId);
            }
            else
            {
                foreach (string cveId in cveIds)
                {
                    if (advisory.ReferencesJson.Contains(cveId, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = byCve[cveId];
                        mappedCveIds.Add(cveId);
                        break;
                    }
                }
            }

            if (entry is null)
                continue;

            advisory.IsKev = true;
            advisory.KevAddedAt = DateTime.SpecifyKind(entry.DateAdded, DateTimeKind.Utc);
            advisory.KevDueDate = entry.DueDate.HasValue
                ? DateTime.SpecifyKind(entry.DueDate.Value, DateTimeKind.Utc)
                : null;
            updated++;
        }

        // Thin advisories for CVEs without any existing advisory row.
        int inserted = 0;
        DateTime now = DateTime.UtcNow;
        HashSet<string> existingKevOnly = (
            await _db
                .Advisories.Where(advisory =>
                    advisory.Feed == Feed.Kev && cveIds.Contains(advisory.ExternalId)
                )
                .Select(advisory => advisory.ExternalId)
                .ToListAsync(ct)
        ).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (KevCatalogEntry entry in entries)
        {
            if (mappedCveIds.Contains(entry.CveId))
                continue;
            if (existingKevOnly.Contains(entry.CveId))
            {
                // Refresh the existing thin advisory.
                Advisory? existing = await _db.Advisories.FirstOrDefaultAsync(
                    advisory => advisory.Feed == Feed.Kev && advisory.ExternalId == entry.CveId,
                    ct
                );
                if (existing is not null)
                {
                    existing.IsKev = true;
                    existing.KevAddedAt = DateTime.SpecifyKind(entry.DateAdded, DateTimeKind.Utc);
                    existing.KevDueDate = entry.DueDate.HasValue
                        ? DateTime.SpecifyKind(entry.DueDate.Value, DateTimeKind.Utc)
                        : null;
                    existing.FetchedAt = now;
                }
                continue;
            }

            string summary = string.IsNullOrWhiteSpace(entry.ShortDescription)
                ? entry.VulnerabilityName ?? $"CISA KEV: {entry.CveId}"
                : entry.ShortDescription!;
            string packageName = string.IsNullOrWhiteSpace(entry.Product)
                ? entry.VendorProject ?? "unknown"
                : entry.Product!;

            _db.Advisories.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    Feed = Feed.Kev,
                    ExternalId = entry.CveId,
                    Ecosystem = Ecosystem.Os,
                    PackageName = packageName,
                    AffectedRangesJson = "[]",
                    Severity = Severity.High,
                    Cvss = null,
                    Summary = summary,
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.SpecifyKind(entry.DateAdded, DateTimeKind.Utc),
                    ModifiedAt = DateTime.SpecifyKind(entry.DateAdded, DateTimeKind.Utc),
                    FetchedAt = now,
                    IsKev = true,
                    KevAddedAt = DateTime.SpecifyKind(entry.DateAdded, DateTimeKind.Utc),
                    KevDueDate = entry.DueDate.HasValue
                        ? DateTime.SpecifyKind(entry.DueDate.Value, DateTimeKind.Utc)
                        : null,
                }
            );
            inserted++;
        }

        await _db.SaveChangesAsync(ct);
        return new(updated, inserted);
    }
}
