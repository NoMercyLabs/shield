using System.Text.Json;
using System.Text.RegularExpressions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Nuget;

public sealed class NugetLockParser : IParser
{
    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        if (IsLockfile(filename))
            return await ParseLockfileAsync(content, ct).ConfigureAwait(false);

        if (IsCsproj(filename))
            return await ParseCsprojAsync(content, ct).ConfigureAwait(false);

        return ParseResult.Ok(
            Array.Empty<InventoryItem>(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["unsupportedFilename"] = filename,
            }
        );
    }

    static bool IsLockfile(string filename) =>
        Path.GetFileName(filename).Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase);

    static bool IsCsproj(string filename) =>
        Path.GetExtension(filename).Equals(".csproj", StringComparison.OrdinalIgnoreCase);

    static async ValueTask<ParseResult> ParseLockfileAsync(Stream content, CancellationToken ct)
    {
        using JsonDocument doc = await JsonDocument
            .ParseAsync(content, cancellationToken: ct)
            .ConfigureAwait(false);
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        if (
            !doc.RootElement.TryGetProperty("dependencies", out JsonElement dependencies)
            || dependencies.ValueKind != JsonValueKind.Object
        )
        {
            diagnostics["error"] = "missingDependenciesSection";
            return ParseResult.Ok(items, diagnostics);
        }

        foreach (JsonProperty framework in dependencies.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (JsonProperty package in framework.Value.EnumerateObject())
            {
                if (package.Value.ValueKind != JsonValueKind.Object)
                    continue;

                string name = package.Name;
                string? resolved = package.Value.TryGetProperty(
                    "resolved",
                    out JsonElement resolvedEl
                )
                    ? resolvedEl.GetString()
                    : null;
                string? type = package.Value.TryGetProperty("type", out JsonElement typeEl)
                    ? typeEl.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(resolved))
                    continue;

                bool isDirect = string.Equals(type, "Direct", StringComparison.OrdinalIgnoreCase);

                items.Add(
                    new InventoryItem
                    {
                        Ecosystem = Ecosystem.Nuget,
                        Name = name,
                        Version = resolved!,
                        ParentChain = "[]",
                        IsDirect = isDirect,
                    }
                );
            }
        }

        return ParseResult.Ok(items, diagnostics);
    }

    static async ValueTask<ParseResult> ParseCsprojAsync(Stream content, CancellationToken ct)
    {
        using StreamReader reader = new(content);
        string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["lockfileMissing"] = "true",
        };

        MatchCollection matches = Regex.Matches(
            text,
            "<PackageReference\\s+[^>]*Include\\s*=\\s*\"(?<name>[^\"]+)\"[^>]*Version\\s*=\\s*\"(?<version>[^\"]+)\"",
            RegexOptions.IgnoreCase
        );

        foreach (Match match in matches)
        {
            string name = match.Groups["name"].Value;
            string version = match.Groups["version"].Value;
            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Nuget,
                    Name = name,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        return ParseResult.Ok(items, diagnostics);
    }
}
