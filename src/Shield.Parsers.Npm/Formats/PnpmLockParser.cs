using Shield.Core.Domain;
using Shield.Core.Results;
using YamlDotNet.RepresentationModel;

namespace Shield.Parsers.Npm.Formats;

internal static class PnpmLockParser
{
    public static ParseResult Parse(string text)
    {
        YamlStream stream = new();
        using StringReader sr = new(text);
        try
        {
            stream.Load(sr);
        }
        catch (Exception ex)
        {
            return ParseResult.Fail($"pnpm-lock.yaml: invalid YAML ({ex.Message})");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return ParseResult.Fail("pnpm-lock.yaml: empty document");
        }

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["format"] = "pnpm-lock.yaml"
        };

        HashSet<string> directNamesAtVersion = new(StringComparer.Ordinal);

        if (root.Children.TryGetValue(new YamlScalarNode("importers"), out YamlNode? importersNode) && importersNode is YamlMappingNode importers)
        {
            foreach (KeyValuePair<YamlNode, YamlNode> kv in importers.Children)
            {
                if (kv.Value is not YamlMappingNode importer)
                {
                    continue;
                }
                CollectDirectsFromImporter(importer, directNamesAtVersion);
            }
        }
        else
        {
            CollectDirectsFromImporter(root, directNamesAtVersion);
        }

        YamlMappingNode? source = null;
        if (root.Children.TryGetValue(new YamlScalarNode("packages"), out YamlNode? packagesNode) && packagesNode is YamlMappingNode pkgs)
        {
            source = pkgs;
            diagnostics["section"] = "packages";
        }
        else if (root.Children.TryGetValue(new YamlScalarNode("snapshots"), out YamlNode? snapshotsNode) && snapshotsNode is YamlMappingNode snaps)
        {
            source = snaps;
            diagnostics["section"] = "snapshots";
        }

        if (source is null)
        {
            return ParseResult.Fail("pnpm-lock.yaml: neither 'packages' nor 'snapshots' section found");
        }

        foreach (KeyValuePair<YamlNode, YamlNode> kv in source.Children)
        {
            if (kv.Key is not YamlScalarNode keyScalar || keyScalar.Value is null)
            {
                continue;
            }
            (string name, string version) = ExtractNameVersion(keyScalar.Value);
            if (name.Length == 0 || version.Length == 0)
            {
                continue;
            }
            bool isDirect = directNamesAtVersion.Contains($"{name}@{version}");
            items.Add(new InventoryItem
            {
                Ecosystem = Ecosystem.Npm,
                Name = name,
                Version = version,
                ParentChain = "[]",
                IsDirect = isDirect,
            });
        }

        return ParseResult.Ok(items, diagnostics);
    }

    private static void CollectDirectsFromImporter(YamlMappingNode importer, HashSet<string> bag)
    {
        foreach (string section in new[] { "dependencies", "devDependencies", "optionalDependencies", "peerDependencies" })
        {
            if (!importer.Children.TryGetValue(new YamlScalarNode(section), out YamlNode? secNode) || secNode is not YamlMappingNode sec)
            {
                continue;
            }
            foreach (KeyValuePair<YamlNode, YamlNode> entry in sec.Children)
            {
                if (entry.Key is not YamlScalarNode nameScalar || nameScalar.Value is null)
                {
                    continue;
                }
                string name = nameScalar.Value;
                string? version = null;
                if (entry.Value is YamlScalarNode versionScalar)
                {
                    version = versionScalar.Value;
                }
                else if (entry.Value is YamlMappingNode versionMap
                         && versionMap.Children.TryGetValue(new YamlScalarNode("version"), out YamlNode? vNode)
                         && vNode is YamlScalarNode vScalar)
                {
                    version = vScalar.Value;
                }
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }
                string normalized = StripVersionSuffix(version);
                bag.Add($"{name}@{normalized}");
            }
        }
    }

    private static (string name, string version) ExtractNameVersion(string packageKey)
    {
        string key = packageKey;
        if (key.StartsWith('/'))
        {
            key = key[1..];
        }

        int versionAt;
        if (key.StartsWith('@'))
        {
            versionAt = key.IndexOf('@', 1);
        }
        else
        {
            versionAt = key.IndexOf('@');
        }
        if (versionAt <= 0)
        {
            return (string.Empty, string.Empty);
        }

        string name = key[..versionAt];
        string version = StripVersionSuffix(key[(versionAt + 1)..]);
        return (name, version);
    }

    private static string StripVersionSuffix(string version)
    {
        int paren = version.IndexOf('(');
        if (paren > 0)
        {
            version = version[..paren];
        }
        int underscore = version.IndexOf('_');
        if (underscore > 0)
        {
            version = version[..underscore];
        }
        return version.Trim();
    }
}
