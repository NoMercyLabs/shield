using Shield.Alerter;
using Shield.Core.Results;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ShieldPolicies.Admin)]
[NoApiToken]
public sealed class ChannelsController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly AlertDispatcher _dispatcher;

    public ChannelsController(ShieldDbContext db, AlertDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChannelResponse>>> List(CancellationToken ct)
    {
        List<AlertChannel> channels = await _db.AlertChannels.ToListAsync(ct);
        return Ok(channels.Select(ChannelResponse.From).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelResponse>> Get(Guid id, CancellationToken ct)
    {
        AlertChannel? channel = await _db.AlertChannels.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        return channel is null ? NotFound() : Ok(ChannelResponse.From(channel));
    }

    [HttpPost]
    public async Task<ActionResult<ChannelResponse>> Create(
        [FromBody] CreateChannelRequest request,
        CancellationToken ct
    )
    {
        AlertChannel channel = new()
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Name = request.Name,
            ConfigJsonEncrypted = request.ConfigJson,
            MinSeverity = request.MinSeverity,
            Enabled = request.Enabled,
        };
        _db.AlertChannels.Add(channel);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = channel.Id }, ChannelResponse.From(channel));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChannelResponse>> Update(
        Guid id,
        [FromBody] UpdateChannelRequest request,
        CancellationToken ct
    )
    {
        AlertChannel? channel = await _db.AlertChannels.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        if (channel is null)
            return NotFound();

        channel.Name = request.Name;
        channel.ConfigJsonEncrypted = request.ConfigJson;
        channel.MinSeverity = request.MinSeverity;
        channel.Enabled = request.Enabled;
        await _db.SaveChangesAsync(ct);
        return Ok(ChannelResponse.From(channel));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        AlertChannel? channel = await _db.AlertChannels.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        if (channel is null)
            return NotFound();
        _db.AlertChannels.Remove(channel);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test-send")]
    public async Task<ActionResult<TestSendResponse>> TestSend(Guid id, CancellationToken ct)
    {
        AlertChannel? channel = await _db.AlertChannels.FirstOrDefaultAsync(
            item => item.Id == id,
            ct
        );
        if (channel is null)
            return NotFound();

        Finding sample = new()
        {
            Id = Guid.NewGuid(),
            SourceId = 0,
            InventoryItemId = 0,
            AdvisoryRefId = Guid.Empty,
            Severity = Severity.Low,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "test-" + Guid.NewGuid().ToString("n"),
            Notes = "Shield test alert",
        };

        IReadOnlyList<AlertEvent> events = await _dispatcher.DispatchAsync([sample], [channel], ct);

        bool success = events.All(evt => evt.Status == AlertStatus.Sent);
        string? error = events.FirstOrDefault(evt => evt.Status == AlertStatus.Failed)?.Error;
        int delivered = events.Count(evt => evt.Status == AlertStatus.Sent);
        return Ok(new TestSendResponse(success, delivered, error));
    }
}
