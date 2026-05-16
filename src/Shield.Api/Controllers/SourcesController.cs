using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Contracts;
using Shield.Api.Workers;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class SourcesController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly ScanQueue _scanQueue;

    public SourcesController(ShieldDbContext db, ScanQueue scanQueue)
    {
        _db = db;
        _scanQueue = scanQueue;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourceResponse>>> List(CancellationToken ct)
    {
        List<Source> sources = await _db
            .Sources.OrderByDescending(source => source.UpdatedAt)
            .ToListAsync(ct);
        return Ok(sources.Select(SourceResponse.From).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SourceDetailResponse>> Get(int id, CancellationToken ct)
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        InventorySnapshot? snapshot = await _db
            .InventorySnapshots.Where(item => item.SourceId == id)
            .OrderByDescending(item => item.TakenAt)
            .FirstOrDefaultAsync(ct);

        SnapshotSummary? summary = snapshot is null
            ? null
            : new SnapshotSummary(
                snapshot.Id,
                snapshot.TakenAt,
                snapshot.ContentsSha,
                snapshot.ItemCount
            );

        return Ok(new SourceDetailResponse(SourceResponse.From(source), summary));
    }

    [HttpPost]
    public async Task<ActionResult<SourceResponse>> Create(
        [FromBody] CreateSourceRequest request,
        CancellationToken ct
    )
    {
        DateTime now = DateTime.UtcNow;
        Source source = new()
        {
            Type = request.Type,
            Name = request.Name,
            ConfigJson = request.ConfigJson,
            ScanInterval = request.ScanInterval,
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = source.Id }, SourceResponse.From(source));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<SourceResponse>> Update(
        int id,
        [FromBody] UpdateSourceRequest request,
        CancellationToken ct
    )
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        source.Name = request.Name;
        source.ConfigJson = request.ConfigJson;
        source.ScanInterval = request.ScanInterval;
        source.Enabled = request.Enabled;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(SourceResponse.From(source));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();
        _db.Sources.Remove(source);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:int}/scan-now")]
    public async Task<IActionResult> ScanNow(int id, CancellationToken ct)
    {
        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();
        await _scanQueue.EnqueueAsync(id, ct);
        return Accepted();
    }
}
