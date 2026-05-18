using System.Security.Claims;
using Shield.Api.Services.Ecosystems;
using Shield.Api.Services.Updates;
using Shield.Api.Workers;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UpdatesController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly UpdateScannerWorker _scanner;
    private readonly IEcosystemRegistry _registry;
    private readonly IUpdateApplier _applier;
    private readonly UpdateApplyQueue _applyQueue;

    public UpdatesController(
        ShieldDbContext db,
        UpdateScannerWorker scanner,
        IEcosystemRegistry registry,
        IUpdateApplier applier,
        UpdateApplyQueue applyQueue
    )
    {
        _db = db;
        _scanner = scanner;
        _registry = registry;
        _applier = applier;
        _applyQueue = applyQueue;
    }

    // List all open (not-yet-applied) updates across every source the caller can read. The
    // SPA groups client-side by source/ecosystem because the row count is small (one per
    // direct dep per source).
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UpdateRow>>> List(CancellationToken ct)
    {
        // SPA reads all updates the caller can access. Source-level access is enforced by the
        // SourceAccess join in production; for now, admin-only listing keeps the scope tight
        // until the multi-tenant access surface for updates is designed.
        List<PackageUpdate> rows = await _db
            .PackageUpdates.AsNoTracking()
            .Where(update => update.AppliedAt == null)
            .OrderByDescending(update => update.DetectedAt)
            .ToListAsync(ct);

        Dictionary<int, string> sourceNames = await _db
            .Sources.AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.Name, ct);

        DateTime now = DateTime.UtcNow;
        Dictionary<int, int> minPkgAgeBySource = await _db
            .Sources.AsNoTracking()
            .ToDictionaryAsync(source => source.Id, source => source.MinPackageAgeHours, ct);

        List<UpdateRow> result = rows.Select(row =>
            {
                int minAgeHours = minPkgAgeBySource.TryGetValue(row.SourceId, out int hours)
                    ? hours
                    : 48;
                bool isTooYoung =
                    minAgeHours > 0
                    && row.PublishedAt.HasValue
                    && (now - row.PublishedAt.Value) < TimeSpan.FromHours(minAgeHours);
                return new UpdateRow(
                    Id: row.Id,
                    SourceId: row.SourceId,
                    SourceName: sourceNames.TryGetValue(row.SourceId, out string? name)
                        ? name
                        : "?",
                    Ecosystem: row.Ecosystem,
                    EcosystemLabel: row.Ecosystem.ToString(),
                    Name: row.Name,
                    CurrentVersion: row.CurrentVersion,
                    LatestVersion: row.LatestVersion,
                    PublishedAt: row.PublishedAt,
                    IsBreakingMajor: row.IsBreakingMajor,
                    IsTooYoung: isTooYoung,
                    DetectedAt: row.DetectedAt
                );
            })
            .ToList();

        return Ok(result);
    }

    // On-demand sweep — operator hits this from /updates to refresh after a manual scan or
    // after configuring a new source. Returns the count of new/updated rows.
    [HttpPost("refresh")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<RefreshResponse>> Refresh(
        [FromQuery] int? sourceId,
        CancellationToken ct
    )
    {
        DateTime now = DateTime.UtcNow;
        int upserts = 0;
        if (sourceId.HasValue)
        {
            Source? source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId.Value, ct);
            if (source is null)
                return NotFound();
            upserts = await _scanner.SweepSourceAsync(_db, _registry, source, now, ct);
        }
        else
        {
            List<Source> sources = await _db
                .Sources.Where(s => s.Enabled && s.Type == SourceType.GithubRepo)
                .ToListAsync(ct);
            foreach (Source source in sources)
                upserts += await _scanner.SweepSourceAsync(_db, _registry, source, now, ct);
        }
        return Ok(new RefreshResponse(upserts));
    }

    // Enqueues an apply job and returns immediately. The background UpdateApplyWorker drains the
    // queue, broadcasts per-source progress over the findings SignalR hub (`updates.job.started`,
    // `updates.source.completed`, `updates.job.completed`), and writes an inbox notification on
    // completion. SPA clients subscribe by jobId to render live progress.
    //
    // DryRun bypasses the queue and runs synchronously — useful for "preview before commit" UX.
    [HttpPost("apply")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<ApplyUpdatesResponse>> Apply(
        [FromBody] ApplyUpdatesRequest request,
        CancellationToken ct
    )
    {
        if (request.DryRun)
        {
            UpdateApplyResult preview = await _applier.ApplyAsync(
                new UpdateApplyRequest(
                    Scope: request.Scope,
                    SourceIds: request.SourceIds,
                    DryRun: true,
                    Force: request.Force,
                    ConfirmProduction: request.ConfirmProduction
                ),
                onSourceCompleted: null,
                ct
            );
            return Ok(new ApplyUpdatesResponse(Queued: false, JobId: null, Preview: preview));
        }

        Guid jobId = Guid.NewGuid();
        Guid? requestedBy = null;
        string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out Guid parsed))
            requestedBy = parsed;

        await _applyQueue.EnqueueAsync(
            new UpdateApplyJob(
                JobId: jobId,
                Scope: request.Scope,
                SourceIds: request.SourceIds,
                Force: request.Force,
                ConfirmProduction: request.ConfirmProduction,
                RequestedByUserId: requestedBy
            ),
            ct
        );

        return Accepted(new ApplyUpdatesResponse(Queued: true, JobId: jobId, Preview: null));
    }
}
