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
public sealed class FindingsController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    private readonly ShieldDbContext _shieldDb;
    private readonly FeedsDbContext _feedsDb;

    public FindingsController(ShieldDbContext shieldDb, FeedsDbContext feedsDb)
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
    }

    [HttpGet]
    public async Task<ActionResult<FindingsPage>> List(
        [FromQuery] Severity? severity,
        [FromQuery] int? sourceId,
        [FromQuery] Ecosystem? ecosystem,
        [FromQuery] FindingState? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        IQueryable<Finding> query = _shieldDb.Findings.AsQueryable();
        if (severity.HasValue)
            query = query.Where(finding => finding.Severity == severity.Value);
        if (sourceId.HasValue)
            query = query.Where(finding => finding.SourceId == sourceId.Value);
        if (state.HasValue)
            query = query.Where(finding => finding.State == state.Value);

        if (ecosystem.HasValue)
        {
            IQueryable<int> itemIds = _shieldDb
                .InventoryItems.Where(item => item.Ecosystem == ecosystem.Value)
                .Select(item => item.Id);
            query = query.Where(finding => itemIds.Contains(finding.InventoryItemId));
        }

        int total = await query.CountAsync(ct);
        List<Finding> items = await query
            .OrderByDescending(finding => finding.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(
            new FindingsPage(items.Select(FindingResponse.From).ToList(), total, page, pageSize)
        );
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FindingDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        Finding? finding = await _shieldDb.Findings.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (finding is null)
            return NotFound();

        Advisory? advisory = await _feedsDb.Advisories.FirstOrDefaultAsync(
            item => item.Id == finding.AdvisoryRefId,
            ct
        );
        InventoryItem? item = await _shieldDb.InventoryItems.FirstOrDefaultAsync(
            entry => entry.Id == finding.InventoryItemId,
            ct
        );

        return Ok(new FindingDetailResponse(FindingResponse.From(finding), advisory, item));
    }

    [HttpPost("{id:guid}/ack")]
    public Task<IActionResult> Ack(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Acked, null, ct);

    [HttpPost("{id:guid}/resolve")]
    public Task<IActionResult> Resolve(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Resolved, null, ct);

    [HttpPost("{id:guid}/suppress")]
    public Task<IActionResult> Suppress(
        Guid id,
        [FromBody] SuppressFindingRequest request,
        CancellationToken ct
    ) => UpdateStateAsync(id, FindingState.Suppressed, request.Reason, ct);

    private async Task<IActionResult> UpdateStateAsync(
        Guid id,
        FindingState state,
        string? notes,
        CancellationToken ct
    )
    {
        Finding? finding = await _shieldDb.Findings.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (finding is null)
            return NotFound();

        finding.State = state;
        if (notes is not null)
            finding.Notes = notes;
        await _shieldDb.SaveChangesAsync(ct);
        return Ok(FindingResponse.From(finding));
    }
}
