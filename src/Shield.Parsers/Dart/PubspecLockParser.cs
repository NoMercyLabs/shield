using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using YamlDotNet.RepresentationModel;

namespace Shield.Parsers.Dart;

public sealed class PubspecLockParser : IParser
{
    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        using StreamReader reader = new(content, leaveOpen: true);
        string text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return Parse(text);
    }

    static ParseResult Parse(string text)
    {
        YamlStream stream = new();
        using StringReader sr = new(text);
        try
        {
            stream.Load(sr);
        }
        catch (Exception ex)
        {
            return ParseResult.Fail($"pubspec.lock: invalid YAML ({ex.Message})");
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return ParseResult.Fail("pubspec.lock: empty document");

        if (
            !root.Children.TryGetValue(new YamlScalarNode("packages"), out YamlNode? packagesNode)
            || packagesNode is not YamlMappingNode packages
        )
            return ParseResult.Fail("pubspec.lock: no 'packages' section");

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

        foreach (KeyValuePair<YamlNode, YamlNode> kv in packages.Children)
        {
            if (kv.Key is not YamlScalarNode keyScalar || keyScalar.Value is null)
                continue;
            if (kv.Value is not YamlMappingNode entry)
                continue;

            string name = keyScalar.Value;
            string? version = null;
            string dependency = "transitive";

            if (
                entry.Children.TryGetValue(new YamlScalarNode("version"), out YamlNode? vNode)
                && vNode is YamlScalarNode vScalar
            )
                version = vScalar.Value;
            if (
                entry.Children.TryGetValue(new YamlScalarNode("dependency"), out YamlNode? dNode)
                && dNode is YamlScalarNode dScalar
                && dScalar.Value is { } dValue
            )
                dependency = dValue;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                continue;

            // pub `dependency` values: "direct main", "direct dev", "direct overridden",
            // "transitive". Anything starting with "direct" is a top-level dep.
            bool isDirect = dependency.StartsWith("direct", StringComparison.OrdinalIgnoreCase);

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Pub,
                    Name = name,
                    Version = version!,
                    ParentChain = "[]",
                    IsDirect = isDirect,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }
}
