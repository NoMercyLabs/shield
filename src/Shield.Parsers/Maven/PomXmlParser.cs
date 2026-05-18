using System.Xml.Linq;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Maven;

// pom.xml carries the declared (direct) dependencies only. Transitive resolution
// requires running `mvn dependency:tree` which is out of scope for v1.
// TODO(v2): pair this with a tree-output ingester so transitive coverage matches Gradle.
public sealed class PomXmlParser : IParser
{
    public async ValueTask<ParseResult> ParseAsync(
        Stream content,
        string filename,
        CancellationToken ct
    )
    {
        XDocument doc;
        try
        {
            doc = await XDocument.LoadAsync(content, LoadOptions.None, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ParseResult.Fail($"pom.xml: invalid XML ({ex.Message})");
        }

        if (doc.Root is null)
            return ParseResult.Fail("pom.xml: empty document");

        List<InventoryItem> items = new();
        Dictionary<string, string> diagnostics = new(StringComparer.Ordinal)
        {
            ["transitive"] = "notResolved",
        };

        // pom.xml elements live in the http://maven.apache.org/POM/4.0.0 namespace.
        // Match by LocalName so files without the xmlns declaration still parse.
        Dictionary<string, string> properties = ReadProperties(doc.Root);

        foreach (
            XElement dep in doc.Root.Descendants().Where(el => el.Name.LocalName == "dependency")
        )
        {
            // Skip dependency entries nested inside <dependencyManagement> — those are
            // version pins, not direct deps, and including them would inflate the list.
            if (dep.Ancestors().Any(a => a.Name.LocalName == "dependencyManagement"))
                continue;

            string? groupId = dep.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "groupId")
                ?.Value?.Trim();
            string? artifactId = dep.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "artifactId")
                ?.Value?.Trim();
            string? version = dep.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "version")
                ?.Value?.Trim();

            groupId = ResolveProperty(groupId, properties);
            artifactId = ResolveProperty(artifactId, properties);
            version = ResolveProperty(version, properties);

            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(artifactId))
                continue;
            // Unresolved version means the version came from a parent POM or BOM we can't see.
            if (string.IsNullOrWhiteSpace(version))
                continue;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Maven,
                    Name = $"{groupId}:{artifactId}",
                    Version = version!,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }

        if (items.Count == 0)
            diagnostics["error"] = "noPackagesFound";

        return ParseResult.Ok(items, diagnostics);
    }

    static Dictionary<string, string> ReadProperties(XElement root)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        XElement? props = root.Elements().FirstOrDefault(e => e.Name.LocalName == "properties");
        if (props is null)
            return result;
        foreach (XElement prop in props.Elements())
            result[prop.Name.LocalName] = prop.Value?.Trim() ?? string.Empty;
        return result;
    }

    static string? ResolveProperty(string? value, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (
            !value.StartsWith("${", StringComparison.Ordinal)
            || !value.EndsWith("}", StringComparison.Ordinal)
        )
            return value;
        string key = value[2..^1];
        return properties.TryGetValue(key, out string? resolved) ? resolved : value;
    }
}
