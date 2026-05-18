using System.Text.Json;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Npm.Formats;

internal static class PackageLockParser
{
    public static ParseResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"package-lock.json: invalid JSON ({ex.Message})");
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;

            int lockfileVersion = root.TryGetProperty("lockfileVersion", out JsonElement lv) && lv.ValueKind == JsonValueKind.Number
                ? lv.GetInt32()
                : 1;

            List<InventoryItem> items = new();
            Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
            {
                ["lockfileVersion"] = lockfileVersion.ToString()
            };

            if (lockfileVersion >= 2 && root.TryGetProperty("packages", out JsonElement packages) && packages.ValueKind == JsonValueKind.Object)
            {
                ParseV2(packages, items);
            }
            else if (root.TryGetProperty("dependencies", out JsonElement deps) && deps.ValueKind == JsonValueKind.Object)
            {
                ParseV1(deps, parents: new List<string>(), isDirect: true, items);
            }
            else
            {
                return ParseResult.Fail("package-lock.json missing both 'packages' (v2/v3) and 'dependencies' (v1) sections");
            }

            return ParseResult.Ok(items, diagnostics);
        }
    }

    private static void ParseV2(JsonElement packages, List<InventoryItem> items)
    {
        // v2/v3 uses a flat map keyed by node_modules path. Root project is key "".
        // Direct deps live in root entry's dep sections.
        HashSet<string> directNames = new(StringComparer.Ordinal);
        if (packages.TryGetProperty("", out JsonElement rootEntry) && rootEntry.ValueKind == JsonValueKind.Object)
        {
            CollectDepNames(rootEntry, "dependencies", directNames);
            CollectDepNames(rootEntry, "devDependencies", directNames);
            CollectDepNames(rootEntry, "optionalDependencies", directNames);
            CollectDepNames(rootEntry, "peerDependencies", directNames);
        }

        foreach (JsonProperty entry in packages.EnumerateObject())
        {
            if (entry.Name.Length == 0)
            {
                continue;
            }

            string path = entry.Name;
            string name = ExtractNameFromPath(path);
            if (name.Length == 0)
            {
                continue;
            }

            JsonElement value = entry.Value;
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (value.TryGetProperty("link", out JsonElement linkEl) && linkEl.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            string version = value.TryGetProperty("version", out JsonElement vEl) && vEl.ValueKind == JsonValueKind.String
                ? (vEl.GetString() ?? string.Empty)
                : string.Empty;

            IReadOnlyList<string> parents = BuildParentChainFromPath(path);
            bool isDirect = CountNodeModulesSegments(path) == 1 && directNames.Contains(name);

            items.Add(new InventoryItem
            {
                Ecosystem = Ecosystem.Npm,
                Name = name,
                Version = version,
                ParentChain = ParentChain.Encode(parents),
                IsDirect = isDirect,
            });
        }
    }

    private static void ParseV1(JsonElement deps, List<string> parents, bool isDirect, List<InventoryItem> items)
    {
        foreach (JsonProperty prop in deps.EnumerateObject())
        {
            string name = prop.Name;
            JsonElement value = prop.Value;
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string version = value.TryGetProperty("version", out JsonElement vEl) && vEl.ValueKind == JsonValueKind.String
                ? (vEl.GetString() ?? string.Empty)
                : string.Empty;

            items.Add(new InventoryItem
            {
                Ecosystem = Ecosystem.Npm,
                Name = name,
                Version = version,
                ParentChain = ParentChain.Encode(parents),
                IsDirect = isDirect,
            });

            if (value.TryGetProperty("dependencies", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
            {
                List<string> nextParents = new(parents) { name };
                ParseV1(nested, nextParents, isDirect: false, items);
            }
        }
    }

    private static void CollectDepNames(JsonElement obj, string property, HashSet<string> bag)
    {
        if (!obj.TryGetProperty(property, out JsonElement el) || el.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        foreach (JsonProperty p in el.EnumerateObject())
        {
            bag.Add(p.Name);
        }
    }

    private static string ExtractNameFromPath(string path)
    {
        int lastNm = path.LastIndexOf("node_modules/", StringComparison.Ordinal);
        if (lastNm < 0)
        {
            return string.Empty;
        }
        string tail = path[(lastNm + "node_modules/".Length)..];
        return tail.TrimEnd('/');
    }

    private static IReadOnlyList<string> BuildParentChainFromPath(string path)
    {
        // path: "node_modules/a/node_modules/b/node_modules/c" -> parents = ["a", "b"]
        string[] segments = path.Split('/');
        List<string> parents = new();
        int index = 0;
        while (index < segments.Length)
        {
            if (segments[index] != "node_modules")
            {
                index++;
                continue;
            }
            int nameStart = index + 1;
            if (nameStart >= segments.Length)
            {
                break;
            }
            string parentName = segments[nameStart];
            if (parentName.StartsWith('@') && nameStart + 1 < segments.Length)
            {
                parentName = parentName + "/" + segments[nameStart + 1];
                index = nameStart + 2;
            }
            else
            {
                index = nameStart + 1;
            }
            parents.Add(parentName);
        }
        if (parents.Count <= 1)
        {
            return Array.Empty<string>();
        }
        // Last element is the package itself; everything before it is the chain.
        return parents.Take(parents.Count - 1).ToList();
    }

    private static int CountNodeModulesSegments(string path)
    {
        int count = 0;
        int idx = 0;
        while ((idx = path.IndexOf("node_modules/", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += "node_modules/".Length;
        }
        return count;
    }
}
