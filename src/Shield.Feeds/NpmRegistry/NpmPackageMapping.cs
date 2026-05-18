using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.NpmRegistry;

public static class NpmPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(
        NpmPackageDocument document,
        DateTime fetchedAtUtc,
        long? weeklyDownloads = null
    )
    {
        string maintainersJson = JsonSerializer.Serialize(
            (document.Maintainers ?? []).Select(maintainer => maintainer.Name).ToArray()
        );

        Dictionary<string, NpmVersion> versions = document.Versions ?? [];
        Dictionary<string, DateTime> times = document.Time ?? [];

        foreach (KeyValuePair<string, NpmVersion> entry in versions)
        {
            string versionId = entry.Key;
            NpmVersion versionNode = entry.Value;
            DateTime? publishedAtUtc = times.TryGetValue(versionId, out DateTime parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : null;

            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Npm,
                Name = document.Name,
                Version = versionId,
                PublishedAt = publishedAtUtc,
                MaintainersJson = maintainersJson,
                TarballSha = versionNode.Dist?.Shasum,
                Deprecated = !string.IsNullOrEmpty(versionNode.Deprecated),
                // Downloads are a package-level signal, not per-version. Copying onto every
                // version row keeps the anomaly detector's per-version meta lookup self-
                // contained (no extra join), at the cost of a few redundant longs.
                WeeklyDownloads = weeklyDownloads,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
