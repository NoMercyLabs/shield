using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Python.Formats;

internal static class RequirementsTxtParser
{
    static readonly string[] Operators = { "===", "==", "!=", ">=", "<=", "~=", ">", "<" };

    public static ParseResult Parse(string text)
    {
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);
        int unpinned = 0;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];
            line = line.Trim();
            if (line.Length == 0)
                continue;
            // Skip option lines (-r other.txt, --hash, -e .).
            if (line.StartsWith("-"))
                continue;

            // Strip environment markers (`pkg==1.0; python_version >= '3.8'`).
            int semicolon = line.IndexOf(';');
            if (semicolon >= 0)
                line = line[..semicolon].Trim();
            // Strip extras (`pkg[extra1,extra2]==1.0`).
            int bracket = line.IndexOf('[');
            int bracketClose = line.IndexOf(']');
            if (bracket >= 0 && bracketClose > bracket)
                line = line.Remove(bracket, bracketClose - bracket + 1);

            string? matchedOp = null;
            int opIndex = -1;
            foreach (string op in Operators)
            {
                int idx = line.IndexOf(op, StringComparison.Ordinal);
                if (idx >= 0 && (opIndex < 0 || idx < opIndex))
                {
                    opIndex = idx;
                    matchedOp = op;
                }
            }

            string name;
            string version;
            if (matchedOp is null)
            {
                name = line.Trim();
                version = string.Empty;
                unpinned++;
            }
            else
            {
                name = line[..opIndex].Trim();
                version = line[(opIndex + matchedOp.Length)..].Trim();
                if (matchedOp != "==" && matchedOp != "===")
                {
                    unpinned++;
                    version = string.Empty;
                }
            }

            if (name.Length == 0)
                continue;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Python,
                    Name = name,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        if (unpinned > 0)
            diagnostics["unpinned"] = unpinned.ToString();
        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
