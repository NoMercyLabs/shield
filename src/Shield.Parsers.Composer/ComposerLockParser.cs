using System.Text.Json;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Composer;

public sealed class ComposerLockParser : IParser
{
    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        using JsonDocument doc = await JsonDocument
            .ParseAsync(content, cancellationToken: ct)
            .ConfigureAwait(false);
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        AddPackages(doc.RootElement, "packages", items);
        AddPackages(doc.RootElement, "packages-dev", items);

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }

    static void AddPackages(JsonElement root, string sectionName, List<InventoryItem> items)
    {
        if (
            !root.TryGetProperty(sectionName, out JsonElement section)
            || section.ValueKind != JsonValueKind.Array
        )
            return;

        foreach (JsonElement package in section.EnumerateArray())
        {
            if (package.ValueKind != JsonValueKind.Object)
                continue;

            string? name = package.TryGetProperty("name", out JsonElement nameEl)
                ? nameEl.GetString()
                : null;
            string? version = package.TryGetProperty("version", out JsonElement versionEl)
                ? versionEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                continue;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Composer,
                    Name = name!,
                    Version = version!,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }
    }
}
