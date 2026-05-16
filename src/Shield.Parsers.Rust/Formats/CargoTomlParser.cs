using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Rust.Formats;

// Cargo.toml gives us the set of direct deps but not their pinned versions.
// We emit one item per direct dep with version="" (version comes from Cargo.lock).
internal static class CargoTomlParser
{
    public static ParseResult Parse(string text)
    {
        Dictionary<string, HashSet<string>> sections = CargoTomlReader.ReadSectionKeys(
            text,
            "dependencies",
            "dev-dependencies",
            "build-dependencies"
        );

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, HashSet<string>> section in sections)
        {
            foreach (string name in section.Value)
            {
                if (!emitted.Add(name))
                    continue;
                items.Add(
                    new InventoryItem
                    {
                        Ecosystem = Ecosystem.Rust,
                        Name = name,
                        Version = string.Empty,
                        ParentChain = "[]",
                        IsDirect = true,
                    }
                );
            }
        }

        if (items.Count == 0)
            diagnostics["error"] = "noDependenciesFound";
        diagnostics["unpinned"] = items.Count.ToString();

        return ParseResult.Ok(items, diagnostics);
    }
}
