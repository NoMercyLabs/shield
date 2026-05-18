using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.PyPiRegistry;

public static class PyPiPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(
        string packageName,
        PyPiPackageDocument document,
        DateTime fetchedAtUtc,
        long? weeklyDownloads
    )
    {
        // PyPI's `author` + `maintainer` are free-text strings ("name email"). Collapse the
        // non-empty ones into a single-entry list for the maintainers signal — not great,
        // but every other ecosystem stores a JSON-encoded string array and the anomaly
        // detector treats the count as the signal, not the contents.
        List<string> maintainers = [];
        if (!string.IsNullOrWhiteSpace(document.Info?.Author))
            maintainers.Add(document.Info!.Author!);
        if (!string.IsNullOrWhiteSpace(document.Info?.Maintainer))
            maintainers.Add(document.Info!.Maintainer!);
        string maintainersJson = JsonSerializer.Serialize(maintainers);

        if (document.Releases is null)
            yield break;

        foreach (KeyValuePair<string, List<PyPiRelease>> entry in document.Releases)
        {
            string version = entry.Key;
            if (string.IsNullOrEmpty(version))
                continue;

            // A version can have multiple distribution files (wheel + sdist); they share an
            // upload date. Pick the earliest non-null timestamp as the publish moment.
            DateTime? publishedAt = entry
                .Value.Select(release => release.UploadTime?.UtcDateTime)
                .Where(dt => dt is not null)
                .OrderBy(dt => dt)
                .FirstOrDefault();

            bool yanked = entry.Value.All(release => release.Yanked);

            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Python,
                Name = packageName,
                Version = version,
                PublishedAt = publishedAt,
                MaintainersJson = maintainersJson,
                TarballSha = null,
                Deprecated = yanked,
                WeeklyDownloads = weeklyDownloads,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
