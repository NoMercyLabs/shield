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

    public DashboardController(ShieldDbContext db)
    {
        _db = db;
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

        return Ok(
            new DashboardResponse(
                counts,
                SourcesHealthy: healthy,
                SourcesStale: stale,
                RecentFindings: recent.Select(FindingResponse.From).ToList()
            )
        );
    }
}
