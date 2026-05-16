using System.Text.Json;
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
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 500;
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromHours(1);

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

        SnapshotSummary? summary = null;
        if (snapshot is not null)
        {
            Dictionary<Ecosystem, int> ecosystems = await _db
                .InventoryItems.Where(item => item.SnapshotId == snapshot.Id)
                .GroupBy(item => item.Ecosystem)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, ct);

            summary = new SnapshotSummary(
                snapshot.Id,
                snapshot.TakenAt,
                snapshot.ContentsSha,
                snapshot.ItemCount,
                ecosystems
            );
        }

        return Ok(new SourceDetailResponse(SourceResponse.From(source), summary));
    }

    [HttpPost]
    public async Task<ActionResult<SourceResponse>> Create(
        [FromBody] CreateSourceRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required.");

        if (!TryNormaliseConfig(request.Type, request.ConfigJson, out string configJson, out string? configError))
            return ValidationProblem(configError ?? "Invalid configJson.");

        DateTime now = DateTime.UtcNow;
        Source source = new()
        {
            Type = request.Type,
            Name = request.Name,
            ConfigJson = configJson,
            ScanInterval = request.ScanInterval ?? DefaultScanInterval,
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

        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required.");

        if (!TryNormaliseConfig(source.Type, request.ConfigJson, out string configJson, out string? configError))
            return ValidationProblem(configError ?? "Invalid configJson.");

        source.Name = request.Name;
        source.ConfigJson = configJson;
        source.ScanInterval = request.ScanInterval ?? source.ScanInterval;
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
    public async Task<ActionResult<ScanQueuedResponse>> ScanNow(int id, CancellationToken ct)
    {
        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();
        DateTime queuedAt = DateTime.UtcNow;
        await _scanQueue.EnqueueAsync(id, ct);
        // EstimatedCompletion is nullable for now — worker is single-threaded, no reliable ETA.
        return Accepted(new ScanQueuedResponse(Accepted: true, QueuedAt: queuedAt, EstimatedCompletion: null));
    }

    [HttpGet("{id:int}/snapshots")]
    public async Task<ActionResult<IReadOnlyList<SnapshotListItem>>> Snapshots(
        int id,
        CancellationToken ct
    )
    {
        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();

        List<SnapshotListItem> snapshots = await _db
            .InventorySnapshots.Where(snapshot => snapshot.SourceId == id)
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .Select(snapshot => new SnapshotListItem(
                snapshot.Id,
                snapshot.TakenAt,
                snapshot.ContentsSha,
                snapshot.ItemCount
            ))
            .ToListAsync(ct);

        return Ok(snapshots);
    }

    [HttpGet("{id:int}/snapshots/{snapshotId:guid}/items")]
    public async Task<ActionResult<PagedResponse<InventoryItemResponse>>> SnapshotItems(
        int id,
        Guid snapshotId,
        [FromQuery] Ecosystem? ecosystem,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default
    )
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        InventorySnapshot? snapshot = await _db.InventorySnapshots.FirstOrDefaultAsync(
            entry => entry.Id == snapshotId && entry.SourceId == id,
            ct
        );
        if (snapshot is null)
            return NotFound();

        IQueryable<InventoryItem> query = _db.InventoryItems.Where(item =>
            item.SnapshotId == snapshotId
        );

        if (ecosystem.HasValue)
            query = query.Where(item => item.Ecosystem == ecosystem.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string needle = search.Trim();
            query = query.Where(item => EF.Functions.Like(item.Name, $"%{needle}%"));
        }

        int total = await query.CountAsync(ct);
        List<InventoryItemResponse> items = await query
            .OrderBy(item => item.Ecosystem)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new InventoryItemResponse(
                item.Id,
                item.Ecosystem,
                item.Name,
                item.Version,
                item.IsDirect,
                item.ParentChain
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<InventoryItemResponse>(items, total, page, pageSize));
    }

    // Accept ConfigJson as either an object or a JSON-encoded string. Object → serialise.
    // Validates required fields per source type (path for LocalFolder, owner+repo for GithubRepo).
    private static bool TryNormaliseConfig(
        SourceType type,
        JsonElement element,
        out string configJson,
        out string? error
    )
    {
        configJson = "{}";
        error = null;

        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            error = "configJson is required.";
            return false;
        }

        // String input — accept verbatim once we confirm it parses as object.
        if (element.ValueKind == JsonValueKind.String)
        {
            string raw = element.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "configJson is required.";
                return false;
            }
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(raw);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "configJson must encode a JSON object.";
                    return false;
                }
                return ValidateConfig(type, parsed.RootElement, raw, out configJson, out error);
            }
            catch (JsonException ex)
            {
                error = $"configJson is not valid JSON: {ex.Message}";
                return false;
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "configJson must be a JSON object or a JSON-encoded string.";
            return false;
        }

        string serialised = element.GetRawText();
        return ValidateConfig(type, element, serialised, out configJson, out error);
    }

    private static bool ValidateConfig(
        SourceType type,
        JsonElement root,
        string serialised,
        out string configJson,
        out string? error
    )
    {
        configJson = serialised;
        error = null;

        switch (type)
        {
            case SourceType.LocalFolder:
                if (!TryGetString(root, "path", out string? path) || string.IsNullOrWhiteSpace(path))
                {
                    error = "LocalFolder configJson requires a non-empty 'path'.";
                    return false;
                }
                break;
            case SourceType.GithubRepo:
                if (!TryGetString(root, "owner", out string? owner) || string.IsNullOrWhiteSpace(owner))
                {
                    error = "GithubRepo configJson requires a non-empty 'owner'.";
                    return false;
                }
                if (!TryGetString(root, "repo", out string? repo) || string.IsNullOrWhiteSpace(repo))
                {
                    error = "GithubRepo configJson requires a non-empty 'repo'.";
                    return false;
                }
                break;
        }
        return true;
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property))
            return false;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return true;
    }
}
