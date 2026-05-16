using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

    private readonly ShieldDbContext _db;
    private readonly FeedsDbContext _feedsDb;

    public DashboardController(ShieldDbContext db, FeedsDbContext feedsDb)
    {
        _db = db;
        _feedsDb = feedsDb;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct)
    {
        List<Finding> openFindings = await _db
            .Findings.Where(finding => finding.State == FindingState.Open)
            .ToListAsync(ct);

        OpenCounts counts = new(
            Low: openFindings.Count(finding => finding.Severity == Severity.Low),
            Medium: openFindings.Count(finding => finding.Severity == Severity.Medium),
            High: openFindings.Count(finding => finding.Severity == Severity.High),
            Critical: openFindings.Count(finding => finding.Severity == Severity.Critical)
        );

        List<Source> sources = await _db.Sources.ToListAsync(ct);
        DateTime now = DateTime.UtcNow;
        int stale = sources.Count(source =>
            source.LastError is not null
            || source.LastScannedAt is null
            || now - source.LastScannedAt.Value > StaleThreshold
        );
        int healthy = sources.Count - stale;

        List<Finding> recent = await _db
            .Findings.OrderByDescending(finding => finding.LastSeenAt)
            .Take(5)
            .ToListAsync(ct);

        List<FindingResponse> recentEnriched = await EnrichAsync(recent, ct);

        return Ok(
            new DashboardResponse(
                counts,
                SourcesHealthy: healthy,
                SourcesStale: stale,
                RecentFindings: recentEnriched
            )
        );
    }

    private async Task<List<FindingResponse>> EnrichAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return new();

        HashSet<int> sourceIds = findings.Select(finding => finding.SourceId).ToHashSet();
        HashSet<int> itemIds = findings.Select(finding => finding.InventoryItemId).ToHashSet();
        HashSet<Guid> advisoryIds = findings.Select(finding => finding.AdvisoryRefId).ToHashSet();

        Dictionary<int, string> sourceNames = await _db
            .Sources.Where(source => sourceIds.Contains(source.Id))
            .ToDictionaryAsync(source => source.Id, source => source.Name, ct);

        Dictionary<int, InventoryItem> items = await _db
            .InventoryItems.Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        Dictionary<Guid, Advisory> advisories = await _feedsDb
            .Advisories.Where(advisory => advisoryIds.Contains(advisory.Id))
            .ToDictionaryAsync(advisory => advisory.Id, ct);

        List<FindingResponse> result = new(findings.Count);
        foreach (Finding finding in findings)
        {
            items.TryGetValue(finding.InventoryItemId, out InventoryItem? item);
            advisories.TryGetValue(finding.AdvisoryRefId, out Advisory? advisory);
            sourceNames.TryGetValue(finding.SourceId, out string? sourceName);
            result.Add(
                FindingResponse.From(
                    finding,
                    sourceName: sourceName,
                    packageName: item?.Name ?? advisory?.PackageName,
                    packageVersion: item?.Version,
                    ecosystem: item?.Ecosystem ?? advisory?.Ecosystem,
                    advisoryExternalId: advisory?.ExternalId,
                    advisorySummary: advisory?.Summary
                )
            );
        }
        return result;
    }
}
