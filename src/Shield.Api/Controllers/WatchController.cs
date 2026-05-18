using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/watch")]
[Authorize]
public sealed class WatchController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly IAccessResolver _access;

    public WatchController(
        ShieldDbContext db,
        UserManager<ShieldUser> userManager,
        IAccessResolver access
    )
    {
        _db = db;
        _userManager = userManager;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PackageWatchResponse>>> List(CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        List<PackageWatch> rows = await _db
            .PackageWatches.Where(watch => watch.UserId == userId)
            .OrderBy(watch => watch.Ecosystem)
            .ThenBy(watch => watch.PackageName)
            .ToListAsync(ct);
        return Ok(rows.Select(PackageWatchResponse.From).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PackageWatchResponse>> Create(
        [FromBody] CreateWatchRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.PackageName))
            return BadRequest(new { error = "packageName is required" });

        string normalized = request.PackageName.Trim();
        Guid userId = await ResolveUserIdAsync();

        PackageWatch? existing = await _db.PackageWatches.FirstOrDefaultAsync(
            watch =>
                watch.UserId == userId
                && watch.Ecosystem == request.Ecosystem
                && watch.PackageName == normalized,
            ct
        );
        if (existing is not null)
            return Ok(PackageWatchResponse.From(existing));

        PackageWatch row = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Ecosystem = request.Ecosystem,
            PackageName = normalized,
            AddedAt = DateTime.UtcNow,
        };
        _db.PackageWatches.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(PackageWatchResponse.From(row));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        PackageWatch? row = await _db.PackageWatches.FirstOrDefaultAsync(
            watch => watch.Id == id && watch.UserId == userId,
            ct
        );
        if (row is null)
            return NotFound();
        _db.PackageWatches.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<IReadOnlyList<WatchSummaryRow>>> Summary(CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        List<PackageWatch> watches = await _db
            .PackageWatches.Where(watch => watch.UserId == userId)
            .ToListAsync(ct);

        if (watches.Count == 0)
            return Ok(Array.Empty<WatchSummaryRow>());

        bool isAdmin = User.IsInRole(ShieldRoles.Admin);
        IReadOnlyList<int>? visible = isAdmin
            ? null
            : await _access.GetVisibleSourceIdsAsync(User, ct);

        HashSet<(Ecosystem Ecosystem, string Name)> watchKeys = watches
            .Select(watch => (watch.Ecosystem, watch.PackageName))
            .ToHashSet();
        HashSet<Ecosystem> ecosystems = watches.Select(watch => watch.Ecosystem).ToHashSet();
        HashSet<string> names = watches.Select(watch => watch.PackageName).ToHashSet();

        // Pull every inventory item for these (ecosystem, name) pairs in one round-trip,
        // then aggregate in-memory. SourceCount = distinct snapshot.Source. OpenFindings
        // joins back to Findings via InventoryItem id.
        List<InventoryItemAggregate> itemRows = await _db
            .InventoryItems.Where(item =>
                ecosystems.Contains(item.Ecosystem) && names.Contains(item.Name)
            )
            .Join(
                _db.InventorySnapshots,
                item => item.SnapshotId,
                snapshot => snapshot.Id,
                (item, snapshot) =>
                    new InventoryItemAggregate(
                        item.Id,
                        item.Ecosystem,
                        item.Name,
                        snapshot.SourceId
                    )
            )
            .ToListAsync(ct);

        if (visible is not null)
            itemRows = itemRows.Where(row => visible.Contains(row.SourceId)).ToList();

        HashSet<int> itemIds = itemRows.Select(row => row.ItemId).ToHashSet();
        List<Finding> findings =
            itemIds.Count == 0
                ? []
                : await _db
                    .Findings.Where(finding =>
                        finding.State == FindingState.Open
                        && itemIds.Contains(finding.InventoryItemId)
                    )
                    .ToListAsync(ct);

        Dictionary<int, InventoryItemAggregate> itemById = itemRows.ToDictionary(row => row.ItemId);

        List<WatchSummaryRow> result = new(watches.Count);
        foreach (PackageWatch watch in watches.OrderBy(w => w.Ecosystem).ThenBy(w => w.PackageName))
        {
            List<InventoryItemAggregate> matchingItems = itemRows
                .Where(row => row.Ecosystem == watch.Ecosystem && row.Name == watch.PackageName)
                .ToList();
            int sourceCount = matchingItems.Select(row => row.SourceId).Distinct().Count();
            HashSet<int> matchingItemIds = matchingItems.Select(row => row.ItemId).ToHashSet();
            int low = 0,
                medium = 0,
                high = 0,
                critical = 0;
            foreach (Finding finding in findings)
            {
                if (!matchingItemIds.Contains(finding.InventoryItemId))
                    continue;
                switch (finding.Severity)
                {
                    case Severity.Low:
                        low++;
                        break;
                    case Severity.Medium:
                        medium++;
                        break;
                    case Severity.High:
                        high++;
                        break;
                    case Severity.Critical:
                        critical++;
                        break;
                }
            }
            result.Add(
                new(
                    watch.Ecosystem,
                    watch.PackageName,
                    sourceCount,
                    new(low, medium, high, critical)
                )
            );
        }
        return Ok(result);
    }

    private async Task<Guid> ResolveUserIdAsync()
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out Guid parsed))
            return parsed;
        ShieldUser? user = await _userManager.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }

    private sealed record InventoryItemAggregate(
        int ItemId,
        Ecosystem Ecosystem,
        string Name,
        int SourceId
    );
}
