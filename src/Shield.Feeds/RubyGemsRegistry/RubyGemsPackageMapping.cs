using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.RubyGemsRegistry;

public static class RubyGemsPackageMapping
{
    public static IEnumerable<PackageMeta> Expand(
        string gemName,
        RubyGemsGem gem,
        IReadOnlyList<RubyGemsVersion> versions,
        DateTime fetchedAtUtc
    )
    {
        // RubyGems exposes total downloads, not weekly — divide by ~52 weeks of the project's
        // lifetime as a rough weekly equivalent, capped at total/52 so brand-new gems with
        // explosive growth still register a sensible floor.
        long? weekly = gem.Downloads > 0 ? Math.Max(1, gem.Downloads / 52) : null;

        // Per-version author is the version-level string ("name1, name2"). Fall back to the
        // gem-level authors when a version row omits it.
        foreach (RubyGemsVersion entry in versions)
        {
            if (string.IsNullOrEmpty(entry.Number))
                continue;
            string maintainerString = entry.Authors ?? gem.Authors ?? string.Empty;
            List<string> maintainers = SplitAuthors(maintainerString);
            string maintainersJson = JsonSerializer.Serialize(maintainers);

            yield return new PackageMeta
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.RubyGems,
                Name = gemName,
                Version = entry.Number,
                PublishedAt = entry.CreatedAt?.UtcDateTime,
                MaintainersJson = maintainersJson,
                TarballSha = null,
                Deprecated = entry.Yanked,
                WeeklyDownloads = weekly,
                FetchedAt = fetchedAtUtc,
            };
        }
    }

    private static List<string> SplitAuthors(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
}
