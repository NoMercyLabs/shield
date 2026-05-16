using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

/// Reads the latest inventory snapshot per enabled source and exposes the unique
/// (ecosystem, name, version) coordinates that need vulnerability lookups.
public sealed class EfInventoryQuery : IInventoryQuery
{
    private readonly ShieldDbContext _db;

    public EfInventoryQuery(ShieldDbContext db)
    {
        _db = db;
    }

    public async ValueTask<IReadOnlyList<InventoryCoordinate>> GetActiveCoordinatesAsync(
        CancellationToken ct
    )
    {
        List<int> enabledSourceIds = await _db
            .Sources.Where(source => source.Enabled)
            .Select(source => source.Id)
            .ToListAsync(ct);

        if (enabledSourceIds.Count == 0)
            return Array.Empty<InventoryCoordinate>();

        List<InventorySnapshot> snapshots = await _db
            .InventorySnapshots.Where(snapshot => enabledSourceIds.Contains(snapshot.SourceId))
            .ToListAsync(ct);

        HashSet<Guid> latestPerSource = snapshots
            .GroupBy(snapshot => snapshot.SourceId)
            .Select(group => group.OrderByDescending(snapshot => snapshot.TakenAt).First().Id)
            .ToHashSet();

        if (latestPerSource.Count == 0)
            return Array.Empty<InventoryCoordinate>();

        List<InventoryItem> items = await _db
            .InventoryItems.Where(item => latestPerSource.Contains(item.SnapshotId))
            .ToListAsync(ct);

        return items
            .Select(item => new InventoryCoordinate(item.Ecosystem, item.Name, item.Version))
            .Distinct()
            .ToList();
    }
}
