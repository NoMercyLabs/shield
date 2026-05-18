using System.Text.Json;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Parsers.Python.Formats;

internal static class PipfileLockParser
{
    public static ParseResult Parse(string text)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            return ParseResult.Fail($"Pipfile.lock: invalid JSON ({ex.Message})");
        }

        using (doc)
        {
            List<InventoryItem> items = new();
            Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);

            AddSection(doc.RootElement, "default", items);
            AddSection(doc.RootElement, "develop", items);

            if (items.Count == 0)
                diagnostics["error"] = "noPackagesFound";

            return ParseResult.Ok(items, diagnostics);
        }
    }

    static void AddSection(JsonElement root, string sectionName, List<InventoryItem> items)
    {
        if (
            !root.TryGetProperty(sectionName, out JsonElement section)
            || section.ValueKind != JsonValueKind.Object
        )
            return;

        foreach (JsonProperty entry in section.EnumerateObject())
        {
            string packageName = entry.Name;
            if (entry.Value.ValueKind != JsonValueKind.Object)
                continue;

            string version = string.Empty;
            if (
                entry.Value.TryGetProperty("version", out JsonElement vEl)
                && vEl.ValueKind == JsonValueKind.String
            )
            {
                string raw = vEl.GetString() ?? string.Empty;
                version = raw.StartsWith("==") ? raw[2..] : raw;
            }

            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(version))
                continue;

            items.Add(
                new InventoryItem
                {
                    Ecosystem = Ecosystem.Python,
                    Name = packageName,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
        }
    }
}
