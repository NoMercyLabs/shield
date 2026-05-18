using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Api.Services;
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
    private readonly IAccessResolver _access;

    public DashboardController(ShieldDbContext db, FeedsDbContext feedsDb, IAccessResolver access)
    {
        _db = db;
        _feedsDb = feedsDb;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct)
    {
        bool isAdmin = User.IsInRole(ShieldRoles.Admin);
        IReadOnlyList<int>? visible = isAdmin
            ? null
            : await _access.GetVisibleSourceIdsAsync(User, ct);

        IQueryable<Finding> openQuery = _db.Findings.Where(finding =>
            finding.State == FindingState.Open
        );
        if (visible is not null)
            openQuery = openQuery.Where(finding => visible.Contains(finding.SourceId));
        List<Finding> openFindings = await openQuery.ToListAsync(ct);

        OpenCounts counts = new(
            Low: openFindings.Count(finding => finding.Severity == Severity.Low),
            Medium: openFindings.Count(finding => finding.Severity == Severity.Medium),
            High: openFindings.Count(finding => finding.Severity == Severity.High),
            Critical: openFindings.Count(finding => finding.Severity == Severity.Critical)
        );

        IQueryable<Source> sourceQuery = _db.Sources;
        if (visible is not null)
            sourceQuery = sourceQuery.Where(source => visible.Contains(source.Id));
        List<Source> sources = await sourceQuery.ToListAsync(ct);
        DateTime now = DateTime.UtcNow;
        int stale = sources.Count(source =>
            source.LastError is not null
            || source.LastScannedAt is null
            || now - source.LastScannedAt.Value > StaleThreshold
        );
        int healthy = sources.Count - stale;

        IQueryable<Finding> recentQuery = _db.Findings;
        if (visible is not null)
            recentQuery = recentQuery.Where(finding => visible.Contains(finding.SourceId));
        List<Finding> recent = await recentQuery
            .OrderByDescending(finding => finding.LastSeenAt)
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
            return [];

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
