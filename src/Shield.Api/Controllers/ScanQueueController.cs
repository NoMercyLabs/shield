namespace Shield.Api.Controllers;

[ApiController]
[Route("api/scan-queue")]
[Authorize]
public sealed class ScanQueueController : ControllerBase
{
    private readonly ShieldDbContext _db;

    public ScanQueueController(ShieldDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ScanQueueStatusResponse>> Status(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        int pending = await _db.ScanQueueEntries.CountAsync(
            entry => entry.CompletedAt == null && entry.StartedAt == null,
            ct
        );
        int inProgress = await _db.ScanQueueEntries.CountAsync(
            entry => entry.CompletedAt == null && entry.StartedAt != null,
            ct
        );

        // Recent failures: last 10 rows that completed with a NOISE-FILTERED error.
        // "Source no longer exists" is a benign race when an admin deleted the source while a
        // queue row was in flight; "inner handler not assigned" was a transient DI bug fixed
        // in the rate-limit handler. Both are uninteresting historical noise that crowds out
        // the actually-actionable failures (rate limits, 404s, malformed lockfiles).
        List<ScanQueueEntry> recentFailures = await _db
            .ScanQueueEntries.AsNoTracking()
            .Where(entry =>
                entry.CompletedAt != null
                && entry.ErrorMessage != null
                && !entry.ErrorMessage.StartsWith("Source no longer exists")
                && !entry.ErrorMessage.Contains("The inner handler has not been assigned")
            )
            .OrderByDescending(entry => entry.CompletedAt)
            .Take(10)
            .ToListAsync(ct);

        List<ScanQueueFailureItem> failures = recentFailures
            .Select(entry => new ScanQueueFailureItem(
                entry.Id,
                entry.SourceId,
                entry.EnqueuedAt,
                entry.CompletedAt!.Value,
                entry.Attempts,
                entry.ErrorMessage ?? string.Empty
            ))
            .ToList();

        return Ok(new ScanQueueStatusResponse(pending, inProgress, failures));
    }
}
