using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.HexRegistry;

public static class HexPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(HexPackage package, DateTime fetchedAtUtc)
    {
        string maintainersJson = JsonSerializer.Serialize(package.Meta?.Maintainers ?? []);
        if (package.Releases is null)
            yield break;
        foreach (HexRelease release in package.Releases)
        {
            if (string.IsNullOrEmpty(release.Version))
                continue;
            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Hex,
                Name = package.Name,
                Version = release.Version,
                PublishedAt = release.InsertedAt?.UtcDateTime,
                MaintainersJson = maintainersJson,
                TarballSha = null,
                Deprecated = false,
                WeeklyDownloads = package.Downloads?.Week,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
