using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.NugetRegistry;

public static class NugetPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(
        string packageName,
        IReadOnlyList<NugetCatalogEntryRef> versions,
        DateTime fetchedAtUtc,
        long? totalDownloads,
        IReadOnlyList<string>? owners
    )
    {
        // NuGet doesn't expose maintainer emails, just owner handles. Treat owners as the
        // closest proxy for the maintainer-churn signal — a single-owner package is just as
        // suspicious here as a single-maintainer npm package.
        string maintainersJson = JsonSerializer.Serialize(owners ?? []);

        foreach (NugetCatalogEntryRef entry in versions)
        {
            if (string.IsNullOrEmpty(entry.Version))
                continue;
            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Nuget,
                Name = packageName,
                Version = entry.Version,
                PublishedAt = NugetEpochs.Normalize(entry.Published),
                MaintainersJson = maintainersJson,
                TarballSha = null,
                Deprecated = entry.Deprecation is not null,
                // Downloads are package-level, not per-version; copy to every row so the
                // anomaly detector's per-version meta lookup stays a single hit.
                WeeklyDownloads = totalDownloads,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
