using Shield.Api.Workers.Queues;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ShieldPolicies.Admin)]
public sealed class FeedsController : ControllerBase
{
    private readonly FeedsDbContext _db;
    private readonly IEnumerable<IFeedSync> _syncs;
    private readonly FeedRefreshQueue _refreshQueue;

    public FeedsController(
        FeedsDbContext db,
        IEnumerable<IFeedSync> syncs,
        FeedRefreshQueue refreshQueue
    )
    {
        _db = db;
        _syncs = syncs;
        _refreshQueue = refreshQueue;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FeedStatusResponse>>> List(CancellationToken ct)
    {
        List<FeedSyncState> states = await _db.FeedSyncStates.ToListAsync(ct);
        Dictionary<Feed, FeedSyncState> stateByFeed = states.ToDictionary(state => state.Feed);
        HashSet<Feed> registered = _syncs.Select(sync => sync.Feed).ToHashSet();

        Dictionary<Feed, int> advisoryCounts = await _db
            .Advisories.GroupBy(advisory => advisory.Feed)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, ct);

        List<FeedStatusResponse> response = [];
        foreach (Feed feed in registered)
        {
            stateByFeed.TryGetValue(feed, out FeedSyncState? state);
            advisoryCounts.TryGetValue(feed, out int count);
            response.Add(
                new(
                    feed,
                    state?.LastSuccessAt,
                    state?.LastError,
                    state?.NextRunAt ?? DateTime.UtcNow,
                    state?.Cursor,
                    Registered: true,
                    AdvisoryCount: count,
                    RateLimitResetAt: state?.RateLimitResetAt
                )
            );
        }
        return Ok(response);
    }

    [HttpPost("{feed}/refresh")]
    public async Task<IActionResult> Refresh(string feed, CancellationToken ct)
    {
        if (!Enum.TryParse<Feed>(feed, ignoreCase: true, out Feed parsed))
            return BadRequest(new { error = $"Unknown feed '{feed}'" });
        if (!_syncs.Any(sync => sync.Feed == parsed))
            return BadRequest(new { error = $"No IFeedSync registered for {parsed}" });

        await _refreshQueue.EnqueueAsync(parsed.ToString(), ct);
        return Accepted();
    }
}
