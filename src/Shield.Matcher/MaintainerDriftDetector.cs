using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Matcher;

public sealed class MaintainerDriftDetector
{
    private static readonly TimeSpan _newMaintainerWindow = TimeSpan.FromHours(24);

    public IReadOnlyList<Advisory> Detect(
        Ecosystem eco,
        string package,
        PackageMeta? previous,
        PackageMeta current,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(current);

        List<Advisory> advisories = new();
        IReadOnlyList<string> currentMaintainers = ParseMaintainers(current.MaintainersJson);

        if (previous is null)
            return advisories;

        IReadOnlyList<string> previousMaintainers = ParseMaintainers(previous.MaintainersJson);

        List<string> added = currentMaintainers.Except(previousMaintainers, StringComparer.OrdinalIgnoreCase).ToList();
        List<string> removed = previousMaintainers.Except(currentMaintainers, StringComparer.OrdinalIgnoreCase).ToList();

        if (added.Count > 0 && current.PublishedAt is { } publishedAt &&
            publishedAt >= nowUtc - _newMaintainerWindow)
        {
            advisories.Add(BuildAdvisory(
                eco,
                package,
                "new-maintainer-immediate-publish",
                Severity.High,
                $"Package '{package}' added maintainer(s) [{string.Join(", ", added)}] and published within 24h.",
                nowUtc));
        }

        if (removed.Count > 0)
        {
            advisories.Add(BuildAdvisory(
                eco,
                package,
                "maintainer-dropped",
                Severity.Medium,
                $"Package '{package}' lost maintainer(s) [{string.Join(", ", removed)}].",
                nowUtc));
        }

        if (current.Deprecated && !previous.Deprecated)
        {
            advisories.Add(BuildAdvisory(
                eco,
                package,
                "deprecated",
                Severity.Low,
                $"Package '{package}' was marked deprecated since the last sync.",
                nowUtc));
        }

        return advisories;
    }

    private static Advisory BuildAdvisory(
        Ecosystem eco,
        string package,
        string kind,
        Severity severity,
        string summary,
        DateTime nowUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.NpmRegistry,
            ExternalId = $"drift:{package}:{kind}:{nowUtc:yyyyMMddTHHmmss}",
            Ecosystem = eco,
            PackageName = package,
            AffectedRangesJson = "[]",
            Severity = severity,
            Summary = summary,
            ReferencesJson = "[]",
            PublishedAt = nowUtc,
            ModifiedAt = nowUtc,
            FetchedAt = nowUtc,
        };

    private static IReadOnlyList<string> ParseMaintainers(string maintainersJson)
    {
        if (string.IsNullOrWhiteSpace(maintainersJson))
            return Array.Empty<string>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(maintainersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            List<string> maintainers = new();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    string? value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        maintainers.Add(value);
                }
                else if (element.ValueKind == JsonValueKind.Object &&
                         element.TryGetProperty("name", out JsonElement nameEl) &&
                         nameEl.ValueKind == JsonValueKind.String)
                {
                    string? value = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        maintainers.Add(value);
                }
            }
            return maintainers;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
