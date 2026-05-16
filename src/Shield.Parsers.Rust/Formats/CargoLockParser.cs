using System.Text.Json;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Rust.Formats;

internal static class CargoLockParser
{
    public static ParseResult Parse(string text)
    {
        List<CargoPackage> packages = CargoTomlReader.ReadPackages(text);
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (CargoPackage pkg in packages)
        {
            if (string.IsNullOrWhiteSpace(pkg.Name) || string.IsNullOrWhiteSpace(pkg.Version))
                continue;
            // Local path deps have no source — skip; OSV can't match them anyway.
            if (string.IsNullOrEmpty(pkg.Source))
                continue;

            string parentChain = "[]";
            if (pkg.Dependencies is { Count: > 0 })
            {
                List<string> names = new();
                foreach (string dep in pkg.Dependencies)
                {
                    // Entry can be "name", "name version", or "name version (registry+...)".
                    string first = dep.Split(' ', 2)[0];
                    if (first.Length > 0)
                        names.Add(first);
                }
                if (names.Count > 0)
                    parentChain = JsonSerializer.Serialize(names);
            }

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Rust,
                    Name = pkg.Name,
                    Version = pkg.Version,
                    ParentChain = parentChain,
                    IsDirect = true,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
