using System.Text.RegularExpressions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Gradle;

public sealed class GradleLockfileParser : IParser
{
    static readonly Regex BuildGradleImplementation = new(
        "(?:implementation|api|runtimeOnly|compileOnly|testImplementation|testRuntimeOnly)\\s*[\\(]?\\s*[\"']([^\"':]+):([^\"':]+):([^\"']+)[\"']",
        RegexOptions.Compiled
    );

    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        using StreamReader reader = new(content);
        string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        string lower = Path.GetFileName(filename).ToLowerInvariant();

        if (lower == "gradle.lockfile")
            return ParseLockfile(text);

        if (lower is "build.gradle" or "build.gradle.kts")
            return ParseBuildGradle(text);

        return ParseResult.Ok(
            Array.Empty<InventoryItem>(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["unsupportedFilename"] = filename,
            }
        );
    }

    static ParseResult ParseLockfile(string text)
    {
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("empty="))
                continue;

            int equalsIndex = line.IndexOf('=');
            string coordinate = equalsIndex >= 0 ? line[..equalsIndex] : line;
            string[] parts = coordinate.Split(':');
            if (parts.Length < 3)
                continue;

            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Gradle,
                    Name = $"{group}:{artifact}",
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        return ParseResult.Ok(items, diagnostics);
    }

    static ParseResult ParseBuildGradle(string text)
    {
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["lockfileMissing"] = "true",
        };

        foreach (Match match in BuildGradleImplementation.Matches(text))
        {
            string group = match.Groups[1].Value;
            string artifact = match.Groups[2].Value;
            string version = match.Groups[3].Value;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Gradle,
                    Name = $"{group}:{artifact}",
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        return ParseResult.Ok(items, diagnostics);
    }
}
