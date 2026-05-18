using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.PackagistRegistry;

public static class PackagistPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(PackagistPackage package, DateTime fetchedAtUtc)
    {
        // Packagist reports monthly downloads; divide by ~4.33 weeks for the weekly signal.
        long? weekly =
            package.Downloads?.Monthly is { } monthly && monthly > 0
                ? (long)(monthly / 4.33)
                : null;

        List<string> maintainers =
            package
                .Maintainers?.Select(maintainer => maintainer.Name ?? string.Empty)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList()
            ?? [];
        string maintainersJson = JsonSerializer.Serialize(maintainers);

        if (package.Versions is null)
            yield break;

        foreach (KeyValuePair<string, PackagistVersion> entry in package.Versions)
        {
            string version = entry.Key;
            if (string.IsNullOrEmpty(version))
                continue;

            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Composer,
                Name = package.Name,
                Version = version,
                PublishedAt = entry.Value.Time?.UtcDateTime,
                MaintainersJson = maintainersJson,
                TarballSha = null,
                // `abandoned` is either bool false or a string (replacement package name).
                // Either form being non-null/non-false means the version is deprecated.
                Deprecated = entry.Value.Abandoned is not null,
                WeeklyDownloads = weekly,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
