using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

// Sources package names to sync from InventoryItems — i.e. "what packages do my users
// actually have". Avoids the obvious foot-gun of pulling the entire registry; we only fetch
// metadata for packages someone in this Shield instance is depending on.
//
// Constructed per ecosystem because IPackageNameSource.GetPackageNamesAsync takes no args
// (the registry-feed contract assumes a feed knows what ecosystem it serves).
public sealed class EfPackageNameSource : IPackageNameSource
{
    private readonly ShieldDbContext _db;
    private readonly Ecosystem _ecosystem;

    public EfPackageNameSource(ShieldDbContext db, Ecosystem ecosystem)
    {
        _db = db;
        _ecosystem = ecosystem;
    }

    public async ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken ct)
    {
        // Distinct on Name across all current snapshots. We're after a name list to fetch
        // metadata for, not the inventory dump itself — duplicates would burn API quota.
        List<string> names = await _db
            .InventoryItems.Where(item => item.Ecosystem == _ecosystem)
            .Select(item => item.Name)
            .Distinct()
            .ToListAsync(ct);
        return names;
    }
}
