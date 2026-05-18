using System.Text.Json;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Swift;

public sealed class PackageResolvedParser : IParser
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

        JsonElement root = doc.RootElement;

        // v2 format: top-level `pins` array.
        // v1 format: nested under `object.pins`.
        JsonElement pins = default;
        bool found = false;
        if (
            root.TryGetProperty("pins", out JsonElement pinsV2)
            && pinsV2.ValueKind == JsonValueKind.Array
        )
        {
            pins = pinsV2;
            found = true;
            diagnostics["format"] = "v2";
        }
        else if (
            root.TryGetProperty("object", out JsonElement obj)
            && obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty("pins", out JsonElement pinsV1)
            && pinsV1.ValueKind == JsonValueKind.Array
        )
        {
            pins = pinsV1;
            found = true;
            diagnostics["format"] = "v1";
        }

        if (!found)
            return ParseResult.Fail("Package.resolved: no pins array found");

        foreach (JsonElement pin in pins.EnumerateArray())
        {
            if (pin.ValueKind != JsonValueKind.Object)
                continue;

            string? identity = pin.TryGetProperty("identity", out JsonElement idEl)
                ? idEl.GetString()
                : null;
            string? location =
                pin.TryGetProperty("location", out JsonElement locEl) ? locEl.GetString()
                : pin.TryGetProperty("repositoryURL", out JsonElement repoEl) ? repoEl.GetString()
                : null;

            if (
                !pin.TryGetProperty("state", out JsonElement state)
                || state.ValueKind != JsonValueKind.Object
            )
                continue;

            string? version = state.TryGetProperty("version", out JsonElement versionEl)
                ? versionEl.GetString()
                : null;
            // Fall back to revision when no version is pinned.
            if (string.IsNullOrEmpty(version))
                version = state.TryGetProperty("revision", out JsonElement revEl)
                    ? revEl.GetString()
                    : null;

            string name = !string.IsNullOrWhiteSpace(location)
                ? location!
                : identity ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                continue;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.SwiftPM,
                    Name = name,
                    Version = version!,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
