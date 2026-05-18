using System.Text.Json;
using Shield.Core.Domain;

namespace Shield.Feeds.Ghsa;

public static class GhsaMapping
{
    public static Severity MapSeverity(string ghsaSeverity) =>
        ghsaSeverity?.ToUpperInvariant() switch
        {
            "LOW" => Severity.Low,
            "MODERATE" => Severity.Medium,
            "HIGH" => Severity.High,
            "CRITICAL" => Severity.Critical,
            _ => Severity.Low,
        };

    public static Ecosystem? MapEcosystem(string ghsaEcosystem) =>
        ghsaEcosystem?.ToUpperInvariant() switch
        {
            "NPM" => Ecosystem.Npm,
            "NUGET" => Ecosystem.Nuget,
            "COMPOSER" => Ecosystem.Composer,
            "MAVEN" => Ecosystem.Gradle,
            "GRADLE" => Ecosystem.Gradle,
            _ => null,
        };

    public static IEnumerable<Advisory> Expand(GhsaAdvisoryNode node, DateTime fetchedAtUtc)
    {
        GhsaVulnerabilityNode[] vulns = node.Vulnerabilities?.Nodes ?? [];
        DateTime publishedUtc = DateTime.SpecifyKind(node.PublishedAt, DateTimeKind.Utc);
        DateTime modifiedUtc = DateTime.SpecifyKind(node.UpdatedAt, DateTimeKind.Utc);
        Severity severity = MapSeverity(node.Severity);
        double? cvss = node.Cvss?.Score;
        string referencesJson = JsonSerializer.Serialize(
            (node.References ?? []).Select(reference => reference.Url).ToArray()
        );

        foreach (GhsaVulnerabilityNode vuln in vulns)
        {
            Ecosystem? eco = MapEcosystem(vuln.Package?.Ecosystem ?? string.Empty);
            if (eco is null)
            {
                continue;
            }

            string affectedRangesJson = JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        range = vuln.VulnerableVersionRange,
                        firstPatchedVersion = vuln.FirstPatchedVersion?.Identifier,
                    },
                }
            );

            yield return new Advisory
            {
                Id = Guid.NewGuid(),
                Feed = Feed.Ghsa,
                ExternalId = node.GhsaId,
                Ecosystem = eco.Value,
                PackageName = vuln.Package?.Name ?? string.Empty,
                AffectedRangesJson = affectedRangesJson,
                Severity = severity,
                Cvss = cvss,
                Summary = node.Summary,
                ReferencesJson = referencesJson,
                PublishedAt = publishedUtc,
                ModifiedAt = modifiedUtc,
                FetchedAt = fetchedAtUtc,
            };
        }
    }
}
