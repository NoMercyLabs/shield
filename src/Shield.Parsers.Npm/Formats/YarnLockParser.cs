using System.Text;
using Shield.Core.Domain;
using Shield.Core.Results;
using YamlDotNet.RepresentationModel;

namespace Shield.Parsers.Npm.Formats;

internal static class YarnLockParser
{
    public static ParseResult Parse(string text)
    {
        bool isBerry = text.Contains("\n__metadata:", StringComparison.Ordinal)
                       || text.StartsWith("__metadata:", StringComparison.Ordinal);

        return isBerry ? ParseBerry(text) : ParseV1(text);
    }

    private static ParseResult ParseV1(string text)
    {
        // yarn.lock v1 is NOT YAML. Each stanza:
        //   "pkg@^1.0.0", "pkg@^1.1.0":
        //     version "1.0.5"
        //     dependencies:
        //       other "^2.0.0"
        // IsDirect can't be derived without package.json — flagged via diagnostic.
        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["format"] = "yarn.lock v1",
            ["isDirectHeuristic"] = "transitive-default (no package.json context)"
        };

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int line = 0;
        while (line < lines.Length)
        {
            string current = lines[line];
            if (current.Length == 0 || current.StartsWith('#') || current[0] == ' ' || current[0] == '\t')
            {
                line++;
                continue;
            }
            if (!current.TrimEnd().EndsWith(':'))
            {
                line++;
                continue;
            }

            string header = current.TrimEnd().TrimEnd(':');
            List<string> keys = SplitYarnHeaderKeys(header);
            line++;

            string? version = null;
            while (line < lines.Length)
            {
                string body = lines[line];
                if (body.Length == 0 || (!body.StartsWith(' ') && !body.StartsWith('\t')))
                {
                    break;
                }
                string trimmed = body.Trim();
                if (trimmed.StartsWith("version ", StringComparison.Ordinal))
                {
                    version = trimmed.Substring("version ".Length).Trim().Trim('"');
                }
                line++;
            }

            if (keys.Count == 0 || version is null)
            {
                continue;
            }

            string packageName = ExtractNameFromYarnKey(keys[0]);
            if (packageName.Length == 0)
            {
                continue;
            }
            items.Add(new InventoryItem
            {
                Ecosystem = Ecosystem.Npm,
                Name = packageName,
                Version = version,
                ParentChain = "[]",
                IsDirect = false,
            });
        }

        return ParseResult.Ok(items, diagnostics);
    }

    private static ParseResult ParseBerry(string text)
    {
        YamlStream stream = new();
        using StringReader sr = new(text);
        try
        {
            stream.Load(sr);
        }
        catch (Exception ex)
        {
            return ParseResult.Fail($"yarn.lock berry: invalid YAML ({ex.Message})");
        }
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return ParseResult.Fail("yarn.lock berry: empty document");
        }

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["format"] = "yarn.lock berry (v2+)",
            ["isDirectHeuristic"] = "transitive-default (no package.json context)"
        };

        foreach (KeyValuePair<YamlNode, YamlNode> kv in root.Children)
        {
            if (kv.Key is not YamlScalarNode keyNode || keyNode.Value is null)
            {
                continue;
            }
            string headerKey = keyNode.Value;
            if (headerKey == "__metadata")
            {
                continue;
            }
            if (kv.Value is not YamlMappingNode entry)
            {
                continue;
            }

            string? version = null;
            if (entry.Children.TryGetValue(new YamlScalarNode("version"), out YamlNode? vNode) && vNode is YamlScalarNode vScalar)
            {
                version = vScalar.Value;
            }
            if (version is null)
            {
                continue;
            }

            List<string> descriptors = SplitYarnHeaderKeys(headerKey);
            string firstName = ExtractNameFromYarnKey(descriptors[0]);
            if (firstName.Length == 0)
            {
                continue;
            }

            items.Add(new InventoryItem
            {
                Ecosystem = Ecosystem.Npm,
                Name = firstName,
                Version = version,
                ParentChain = "[]",
                IsDirect = false,
            });
        }

        return ParseResult.Ok(items, diagnostics);
    }

    private static List<string> SplitYarnHeaderKeys(string header)
    {
        List<string> result = new();
        StringBuilder current = new();
        bool inQuote = false;
        foreach (char c in header)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (c == ',' && !inQuote)
            {
                string seg = current.ToString().Trim();
                if (seg.Length > 0)
                {
                    result.Add(seg);
                }
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        string last = current.ToString().Trim();
        if (last.Length > 0)
        {
            result.Add(last);
        }
        return result;
    }

    private static string ExtractNameFromYarnKey(string key)
    {
        // "@scope/pkg@^1.0.0" -> "@scope/pkg"
        // "pkg@^1.0.0" -> "pkg"
        // "pkg@npm:^1.0.0" -> "pkg"
        string trimmed = key.Trim().Trim('"');
        if (trimmed.StartsWith('@'))
        {
            int second = trimmed.IndexOf('@', 1);
            if (second < 0)
            {
                return trimmed;
            }
            return trimmed[..second];
        }
        int firstAt = trimmed.IndexOf('@');
        if (firstAt <= 0)
        {
            return trimmed;
        }
        return trimmed[..firstAt];
    }
}
