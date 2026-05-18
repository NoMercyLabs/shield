using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.CratesRegistry;

public static class CratesPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(
        string crateName,
        IReadOnlyList<CratesVersion> versions,
        DateTime fetchedAtUtc,
        long? recentDownloads,
        IReadOnlyList<string>? owners
    )
    {
        // Crates.io exposes 90-day recent_downloads; closest to weekly is ~recent/13. Round
        // down on the conservative side — typosquat gates compare to a popularity floor.
        long? weeklyEstimate = recentDownloads.HasValue ? recentDownloads.Value / 13 : null;
        string maintainersJson = JsonSerializer.Serialize(owners ?? []);

        foreach (CratesVersion entry in versions)
        {
            if (string.IsNullOrEmpty(entry.Num))
                continue;
            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Rust,
                Name = crateName,
                Version = entry.Num,
                PublishedAt = entry.CreatedAt?.UtcDateTime,
                MaintainersJson = maintainersJson,
                TarballSha = null,
                Deprecated = entry.Yanked,
                WeeklyDownloads = weeklyEstimate,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
