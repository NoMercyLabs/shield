using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Go.Formats;

internal static class GoModParser
{
    public static ParseResult Parse(string text)
    {
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        bool inRequireBlock = false;

        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw, out string? trailingComment).Trim();
            bool indirect =
                trailingComment is { } c
                && c.Contains("indirect", StringComparison.OrdinalIgnoreCase);

            if (line.Length == 0)
                continue;

            if (inRequireBlock)
            {
                if (line.StartsWith(')'))
                {
                    inRequireBlock = false;
                    continue;
                }
                AddRequireLine(line, indirect, items);
                continue;
            }

            if (line.StartsWith("require", StringComparison.Ordinal) && line.Contains('('))
            {
                inRequireBlock = true;
                continue;
            }
            if (line.StartsWith("require ", StringComparison.Ordinal))
            {
                AddRequireLine(line["require ".Length..].Trim(), indirect, items);
            }
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }

    static string StripComment(string line, out string? comment)
    {
        int slash = line.IndexOf("//", StringComparison.Ordinal);
        if (slash < 0)
        {
            comment = null;
            return line;
        }
        comment = line[(slash + 2)..];
        return line[..slash];
    }

    static void AddRequireLine(string line, bool indirect, List<InventoryItem> items)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return;
        string module = parts[0];
        string version = parts[1];
        if (string.IsNullOrEmpty(module) || string.IsNullOrEmpty(version))
            return;

        items.Add(
            new InventoryItem
            {
                Ecosystem = Ecosystem.Go,
                Name = module,
                Version = version,
                ParentChain = "[]",
                IsDirect = !indirect,
            }
        );
    }
}
