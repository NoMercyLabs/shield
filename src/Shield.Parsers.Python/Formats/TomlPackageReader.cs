using System.Text;

namespace Shield.Parsers.Python.Formats;

// Minimal TOML reader scoped to `[[package]]` arrays-of-tables: extracts top-level
// scalar key/value pairs (name, version, category, source) per table. Skips other
// sections entirely. Not a general TOML parser.
internal static class TomlPackageReader
{
    public static List<PackageTable> ReadPackages(string text)
    {
        List<PackageTable> packages = new();
        PackageTable? current = null;
        bool inPackage = false;
        string subSection = string.Empty;

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string raw = lines[index];
            string line = StripTrailingComment(raw).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[["))
            {
                if (current is not null)
                    packages.Add(current);
                current = null;
                inPackage = false;
                subSection = string.Empty;

                string header = line.Trim('[', ']').Trim();
                if (string.Equals(header, "package", StringComparison.Ordinal))
                {
                    current = new PackageTable();
                    inPackage = true;
                }
                continue;
            }

            if (line.StartsWith("["))
            {
                string header = line.Trim('[', ']').Trim();
                // [package.dependencies] / [package.source] / [package.extras] stay within the
                // current package as sub-tables. Anything else ends the package context.
                if (inPackage && header.StartsWith("package.", StringComparison.Ordinal))
                {
                    subSection = header["package.".Length..];
                    continue;
                }
                if (current is not null)
                    packages.Add(current);
                current = null;
                inPackage = false;
                subSection = string.Empty;
                continue;
            }

            if (!inPackage || current is null)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = line[..eq].Trim();
            string valuePart = line[(eq + 1)..].Trim();

            // Inside [package.dependencies]: each key is a dependency name. We don't care about
            // the constraint value (range/extras/marker), only the name for parent-chain emission.
            if (string.Equals(subSection, "dependencies", StringComparison.Ordinal))
            {
                current.Dependencies ??= new List<string>();
                current.Dependencies.Add(key);
                continue;
            }
            // [package.source] / [package.extras] / other sub-tables: ignore body.
            if (subSection.Length > 0)
                continue;

            if (valuePart.StartsWith("\"\"\""))
            {
                // Skip multiline string blocks.
                int closingIndex = FindMultilineClose(lines, index + 1);
                if (closingIndex > index)
                    index = closingIndex;
                continue;
            }

            if (valuePart.StartsWith("["))
            {
                List<string> arrayItems = ReadInlineOrMultilineArray(lines, ref index, valuePart);
                if (string.Equals(key, "dependencies", StringComparison.Ordinal))
                    current.Dependencies = arrayItems;
                continue;
            }

            if (valuePart.StartsWith("{"))
            {
                // Skip inline table (e.g. source = { ... }), but flag source presence.
                if (string.Equals(key, "source", StringComparison.Ordinal))
                    current.HasSource = true;
                continue;
            }

            string value = UnquoteScalar(valuePart);
            switch (key)
            {
                case "name":
                    current.Name = value;
                    break;
                case "version":
                    current.Version = value;
                    break;
                case "category":
                    current.Category = value;
                    break;
                case "optional":
                    current.Optional = string.Equals(
                        value,
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    );
                    break;
                case "source":
                    current.HasSource = !string.IsNullOrEmpty(value);
                    break;
            }
        }

        if (current is not null)
            packages.Add(current);

        return packages;
    }

    public static Dictionary<string, HashSet<string>> ReadSectionTables(
        string text,
        params string[] sectionNames
    )
    {
        Dictionary<string, HashSet<string>> result = new(StringComparer.Ordinal);
        foreach (string section in sectionNames)
            result[section] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? currentSection = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = StripTrailingComment(raw).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[["))
            {
                currentSection = null;
                continue;
            }
            if (line.StartsWith("["))
            {
                string header = line.Trim('[', ']').Trim();
                currentSection = result.ContainsKey(header) ? header : null;
                continue;
            }

            if (currentSection is null)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim();
            if (key.Length > 0)
                result[currentSection].Add(key);
        }
        return result;
    }

    static string StripTrailingComment(string line)
    {
        bool inString = false;
        StringBuilder sb = new();
        foreach (char c in line)
        {
            if (c == '"')
                inString = !inString;
            if (c == '#' && !inString)
                break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    static int FindMultilineClose(string[] lines, int from)
    {
        for (int i = from; i < lines.Length; i++)
        {
            if (lines[i].Contains("\"\"\""))
                return i;
        }
        return lines.Length - 1;
    }

    static List<string> ReadInlineOrMultilineArray(string[] lines, ref int index, string firstChunk)
    {
        StringBuilder buffer = new(firstChunk);
        while (!ContainsBalancedBrackets(buffer.ToString()) && index + 1 < lines.Length)
        {
            index++;
            buffer.Append(' ').Append(StripTrailingComment(lines[index]).Trim());
        }
        string body = buffer.ToString();
        int open = body.IndexOf('[');
        int close = body.LastIndexOf(']');
        if (open < 0 || close <= open)
            return new List<string>();

        string inner = body.Substring(open + 1, close - open - 1).Trim();
        List<string> items = new();
        if (inner.Length == 0)
            return items;

        foreach (string part in SplitArrayParts(inner))
        {
            string trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;
            items.Add(UnquoteScalar(trimmed));
        }
        return items;
    }

    static IEnumerable<string> SplitArrayParts(string inner)
    {
        bool inString = false;
        int depth = 0;
        int start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '"')
                inString = !inString;
            if (inString)
                continue;
            if (c == '{' || c == '[')
                depth++;
            else if (c == '}' || c == ']')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return inner[start..i];
                start = i + 1;
            }
        }
        if (start <= inner.Length)
            yield return inner[start..];
    }

    static bool ContainsBalancedBrackets(string s)
    {
        int depth = 0;
        bool inString = false;
        foreach (char c in s)
        {
            if (c == '"')
                inString = !inString;
            if (inString)
                continue;
            if (c == '[')
                depth++;
            else if (c == ']')
                depth--;
        }
        return depth <= 0;
    }

    static string UnquoteScalar(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1];
        return value;
    }
}

internal sealed class PackageTable
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Optional { get; set; }
    public bool HasSource { get; set; }
    public List<string>? Dependencies { get; set; }
}
