using System.Text.Json;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Vcpkg;

// vcpkg.json declares the required ports. Concrete versions come from the
// baseline pinned in vcpkg-configuration.json (registry SHA + builtin-baseline)
// — resolving those to concrete port versions requires the vcpkg port tree,
// which we don't have at parse time. v1 emits "*" for unresolvable versions.
// TODO(v2): consume vcpkg-configuration.json baseline + look up versions/<port>.json.
public sealed class VcpkgJsonParser : IParser
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
        if (root.ValueKind != JsonValueKind.Object)
            return ParseResult.Fail("vcpkg.json: root is not an object");

        // Skip vcpkg-configuration.json (no `dependencies`, has `default-registry`).
        if (
            !root.TryGetProperty("dependencies", out JsonElement deps)
            || deps.ValueKind != JsonValueKind.Array
        )
        {
            diagnostics["skipped"] = "noDependenciesArray";
            return ParseResult.Ok(Array.Empty<InventoryItem>(), diagnostics);
        }

        // Surface the baseline so downstream tooling can pin once we resolve versions.
        if (
            root.TryGetProperty("builtin-baseline", out JsonElement baseline)
            && baseline.ValueKind == JsonValueKind.String
        )
            diagnostics["baseline"] = baseline.GetString() ?? string.Empty;

        // Apply a single overrides-table version when present so common cases match OSV.
        Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);
        if (
            root.TryGetProperty("overrides", out JsonElement overridesEl)
            && overridesEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement entry in overridesEl.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;
                string? name = entry.TryGetProperty("name", out JsonElement n)
                    ? n.GetString()
                    : null;
                string? version =
                    entry.TryGetProperty("version", out JsonElement v) ? v.GetString()
                    : entry.TryGetProperty("version-semver", out JsonElement vs) ? vs.GetString()
                    : entry.TryGetProperty("version-string", out JsonElement vstr)
                        ? vstr.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                    overrides[name!] = version!;
            }
        }

        foreach (JsonElement dep in deps.EnumerateArray())
        {
            string? name = null;
            string? version = null;

            if (dep.ValueKind == JsonValueKind.String)
            {
                name = dep.GetString();
            }
            else if (dep.ValueKind == JsonValueKind.Object)
            {
                name = dep.TryGetProperty("name", out JsonElement nameEl)
                    ? nameEl.GetString()
                    : null;
                if (dep.TryGetProperty("version>=", out JsonElement vGteEl))
                    version = vGteEl.GetString();
                else if (dep.TryGetProperty("version", out JsonElement vEl))
                    version = vEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (string.IsNullOrWhiteSpace(version) && overrides.TryGetValue(name!, out string? ov))
                version = ov;
            if (string.IsNullOrWhiteSpace(version))
                version = "*";

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Vcpkg,
                    Name = name!,
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
