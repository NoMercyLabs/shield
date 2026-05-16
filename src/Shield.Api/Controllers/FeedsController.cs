using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Contracts;
using Shield.Api.Workers;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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

        List<FeedStatusResponse> response = new();
        foreach (Feed feed in registered)
        {
            if (stateByFeed.TryGetValue(feed, out FeedSyncState? state))
            {
                response.Add(
                    new FeedStatusResponse(
                        feed,
                        state.LastSuccessAt,
                        state.LastError,
                        state.NextRunAt,
                        state.Cursor,
                        Registered: true
                    )
                );
            }
            else
            {
                response.Add(
                    new FeedStatusResponse(
                        feed,
                        null,
                        null,
                        DateTime.UtcNow,
                        null,
                        Registered: true
                    )
                );
            }
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
