using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Go.Formats;

internal static class GoSumParser
{
    public static ParseResult Parse(string text)
    {
        Dictionary<string, string> seen = new(StringComparer.Ordinal);
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            // Format: <module> <version>[/go.mod] h1:<hash>
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            string module = parts[0];
            string version = parts[1];
            if (version.EndsWith("/go.mod", StringComparison.Ordinal))
                version = version[..^"/go.mod".Length];

            if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(version))
                continue;

            string key = $"{module}@{version}";
            seen[key] = module;
        }

        List<InventoryItem> items = new();
        foreach (KeyValuePair<string, string> entry in seen)
        {
            int at = entry.Key.LastIndexOf('@');
            string version = entry.Key[(at + 1)..];
            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Go,
                    Name = entry.Value,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = false,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
