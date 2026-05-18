using System.Text;

namespace Shield.Parsers.Rust.Formats;

// Minimal TOML reader for Cargo files. Extracts `[[package]]` arrays-of-tables
// (Cargo.lock) plus the set of keys under named single-tables like `[dependencies]`
// (Cargo.toml). Not a general TOML parser.
internal static class CargoTomlReader
{
    public static List<CargoPackage> ReadPackages(string text)
    {
        List<CargoPackage> packages = new();
        CargoPackage? current = null;
        bool inPackage = false;

        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = StripTrailingComment(lines[index]).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                if (current is not null)
                    packages.Add(current);
                current = null;
                inPackage = false;

                string header = line.Trim('[', ']').Trim();
                if (string.Equals(header, "package", StringComparison.Ordinal))
                {
                    current = new CargoPackage();
                    inPackage = true;
                }
                continue;
            }

            if (line.StartsWith('['))
            {
                if (current is not null)
                    packages.Add(current);
                current = null;
                inPackage = false;
                continue;
            }

            if (!inPackage || current is null)
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim();
            string valuePart = line[(eq + 1)..].Trim();

            if (valuePart.StartsWith('['))
            {
                List<string> array = ReadArray(lines, ref index, valuePart);
                if (string.Equals(key, "dependencies", StringComparison.Ordinal))
                    current.Dependencies = array;
                continue;
            }

            string value = Unquote(valuePart);
            switch (key)
            {
                case "name":
                    current.Name = value;
                    break;
                case "version":
                    current.Version = value;
                    break;
                case "source":
                    current.Source = value;
                    break;
            }
        }

        if (current is not null)
            packages.Add(current);

        return packages;
    }

    public static Dictionary<string, HashSet<string>> ReadSectionKeys(
        string text,
        params string[] sectionNames
    )
    {
        Dictionary<string, HashSet<string>> result = new(StringComparer.Ordinal);
        foreach (string s in sectionNames)
            result[s] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? currentSection = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = StripTrailingComment(raw).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                currentSection = null;
                continue;
            }
            if (line.StartsWith('['))
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

    static List<string> ReadArray(string[] lines, ref int index, string firstChunk)
    {
        StringBuilder buffer = new(firstChunk);
        while (!Balanced(buffer.ToString()) && index + 1 < lines.Length)
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
        foreach (string part in SplitParts(inner))
        {
            string trimmed = Unquote(part.Trim());
            if (trimmed.Length > 0)
                items.Add(trimmed);
        }
        return items;
    }

    static IEnumerable<string> SplitParts(string inner)
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

    static bool Balanced(string s)
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

    static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }
}

internal sealed class CargoPackage
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<string>? Dependencies { get; set; }
}
