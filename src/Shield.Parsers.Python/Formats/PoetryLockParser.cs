using System.Text.Json;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Python.Formats;

// Handles poetry.lock, pdm.lock, and uv.lock — all share the `[[package]]` shape.
internal static class PoetryLockParser
{
    public static ParseResult Parse(string text)
    {
        List<PackageTable> tables = TomlPackageReader.ReadPackages(text);
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (PackageTable table in tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name) || string.IsNullOrWhiteSpace(table.Version))
                continue;

            // Poetry marks main vs dev via `category`; pdm/uv may omit it.
            bool isDirect =
                string.IsNullOrEmpty(table.Category)
                || string.Equals(table.Category, "main", StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.Category, "dev", StringComparison.OrdinalIgnoreCase);

            string parentChain = "[]";
            if (table.Dependencies is { Count: > 0 })
            {
                List<string> names = new();
                foreach (string dep in table.Dependencies)
                {
                    string depName = ExtractDependencyName(dep);
                    if (depName.Length > 0)
                        names.Add(depName);
                }
                if (names.Count > 0)
                    parentChain = JsonSerializer.Serialize(names);
            }

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Python,
                    Name = table.Name,
                    Version = table.Version,
                    ParentChain = parentChain,
                    IsDirect = isDirect,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }

    static string ExtractDependencyName(string raw)
    {
        // Entry is either "pkgname" or `pkgname = "^1.0"` style fragments split out of an inline table.
        string trimmed = raw.Trim().Trim('"', '\'');
        int eq = trimmed.IndexOf('=');
        if (eq > 0)
            trimmed = trimmed[..eq].Trim();
        int space = trimmed.IndexOf(' ');
        if (space > 0)
            trimmed = trimmed[..space];
        return trimmed;
    }
}
