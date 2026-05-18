using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

// Generic over an IEcosystemTag — the ecosystem the source filters on is encoded in the
// type, not a runtime constructor argument. Use site reads as
// `new EfPackageNameSource<EcosystemTag.Npm>(db)` so the ecosystem can't drift from the
// FeedSync that consumes it.
//
// Pulls distinct package names from InventoryItems — the registry sync only fetches
// metadata for packages someone in this Shield instance actually depends on; we don't
// crawl the entire registry.
public sealed class EfPackageNameSource<TTag> : IPackageNameSource
    where TTag : struct, IEcosystemTag
{
    private readonly ShieldDbContext _db;

    public EfPackageNameSource(ShieldDbContext db)
    {
        _db = db;
    }

    public async ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken ct)
    {
        // Hoist TTag.Value into a local — EF Core can't translate static-abstract interface
        // member accesses inside an expression tree (CS8927).
        Ecosystem ecosystem = TTag.Value;
        List<string> names = await _db
            .InventoryItems.Where(item => item.Ecosystem == ecosystem)
            .Select(item => item.Name)
            .Distinct()
            .ToListAsync(ct);
        return names;
    }
}
