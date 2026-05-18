using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Data;

// Upserts package metadata into FeedsDbContext.PackageMetas keyed by (Ecosystem, Name, Version).
// Replaces InMemoryPackageMetaSink, which silently dropped every npm-registry sync since
// launch — the in-memory list had no reader and evaporated on every restart.
public sealed class EfPackageMetaSink : IPackageMetaSink
{
    private readonly FeedsDbContext _db;

    public EfPackageMetaSink(FeedsDbContext db)
    {
        _db = db;
    }

    public async ValueTask UpsertAsync(IReadOnlyList<PackageMeta> packages, CancellationToken ct)
    {
        if (packages.Count == 0)
            return;

        // Dedupe in-memory first — the npm registry doc can legitimately list the same
        // version twice when a tag and the version label collide, and the unique index on
        // (Ecosystem, Name, Version) would otherwise blow up on SaveChanges.
        Dictionary<(Ecosystem, string, string), PackageMeta> incomingByKey = new();
        foreach (PackageMeta package in packages)
            incomingByKey[(package.Ecosystem, package.Name, package.Version)] = package;

        HashSet<Ecosystem> ecosystems = incomingByKey.Keys.Select(key => key.Item1).ToHashSet();
        HashSet<string> names = incomingByKey.Keys.Select(key => key.Item2).ToHashSet();
        HashSet<string> versions = incomingByKey.Keys.Select(key => key.Item3).ToHashSet();

        // Load every potential collision in one query. The unique index covers all three
        // columns so the WHERE clause is index-only.
        List<PackageMeta> existingRows = await _db
            .PackageMetas.Where(meta =>
                ecosystems.Contains(meta.Ecosystem)
                && names.Contains(meta.Name)
                && versions.Contains(meta.Version)
            )
            .ToListAsync(ct);

        Dictionary<(Ecosystem, string, string), PackageMeta> existingByKey = existingRows
            .GroupBy(meta => (meta.Ecosystem, meta.Name, meta.Version))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (KeyValuePair<(Ecosystem, string, string), PackageMeta> entry in incomingByKey)
        {
            PackageMeta incoming = entry.Value;
            if (existingByKey.TryGetValue(entry.Key, out PackageMeta? row))
            {
                row.PublishedAt = incoming.PublishedAt ?? row.PublishedAt;
                row.MaintainersJson = incoming.MaintainersJson;
                row.TarballSha = incoming.TarballSha ?? row.TarballSha;
                row.Deprecated = incoming.Deprecated;
                // Downloads: never overwrite a known value with null. Feeds that don't
                // populate downloads (e.g. ecosystems with no popularity API) shouldn't
                // wipe data a previous npm-style sync managed to collect.
                row.WeeklyDownloads = incoming.WeeklyDownloads ?? row.WeeklyDownloads;
                row.FetchedAt = incoming.FetchedAt;
            }
            else
            {
                if (incoming.Id == Guid.Empty)
                    incoming.Id = Guid.NewGuid();
                _db.PackageMetas.Add(incoming);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
