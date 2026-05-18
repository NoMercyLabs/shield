using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shield.Api.Auth;
using Shield.Api.Workers;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ShieldPolicies.Admin)]
public sealed class MatcherController : ControllerBase
{
    private readonly MatchQueue _queue;

    public MatcherController(MatchQueue queue)
    {
        _queue = queue;
    }

    // Force a full re-match across every source's latest snapshot. The worker will pull
    // OSV advisories for each unique (ecosystem, package, version) in inventory before
    // running the in-memory matcher, so this is the call to make right after a scan finishes
    // when you don't want to wait for the next scheduled FeedSyncWorker tick.
    [HttpPost("run-now")]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        await _queue.EnqueueAsync(new(null, null, MatchAll: true), ct);
        return Accepted();
    }
}
