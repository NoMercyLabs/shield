using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shield.Api.Services.ManifestEditors;

// Bumps a dep's `^x.y.z` style version in package.json.
// For direct deps: rewrites the entry in dependencies/devDependencies.
// For transitive deps: injects/updates an "overrides" block so npm hoists the safe version.
// Does not run `npm install` — the response carries a `followUpCommand` hint.
public sealed class NpmManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Npm;

    public ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion)
    {
        string packageName = item.Name;
        string manifestRelative = !string.IsNullOrWhiteSpace(item.ManifestPath)
            ? item.ManifestPath.Replace('/', Path.DirectorySeparatorChar)
            : "package.json";
        string manifest = Path.Combine(rootPath, manifestRelative);
        if (!File.Exists(manifest))
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: $"No package.json at source root ({rootPath}).",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        string source = File.ReadAllText(manifest);
        string escaped = Regex.Escape(packageName);
        Regex pattern = new($"(\"{escaped}\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled);

        string packageDir = Path.GetDirectoryName(manifest) ?? rootPath;
        string followUpCommand = PickInstallCommand(packageDir);
        IReadOnlyList<string> lockfiles = CollectLockfiles(packageDir);
        IReadOnlyList<string> installedDirs = CollectInstalledDirs(packageDir);

        Match match = pattern.Match(source);
        if (match.Success)
        {
            string replacement =
                $"{match.Groups[1].Value}^{suggestedVersion}{match.Groups[3].Value}";
            string updated = pattern.Replace(source, replacement, count: 1);

            if (string.Equals(updated, source, StringComparison.Ordinal))
            {
                return new(
                    ChangedFiles: [],
                    FollowUpCommand: null,
                    UnsupportedReason: "Already at suggested version.",
                    CleanedFiles: [],
                    CleanedDirectories: []
                );
            }

            File.WriteAllText(manifest, updated);
            return new(
                ChangedFiles: [manifest],
                FollowUpCommand: followUpCommand,
                UnsupportedReason: null,
                CleanedFiles: lockfiles,
                CleanedDirectories: installedDirs
            );
        }

        // No direct match. For transitive deps, inject an overrides block.
        if (!item.IsDirect)
        {
            string? patched = InjectOverride(source, packageName, suggestedVersion);
            if (patched is null)
            {
                return new(
                    ChangedFiles: [],
                    FollowUpCommand: null,
                    UnsupportedReason: $"Package '{packageName}' is transitive but package.json could not be parsed for overrides injection.",
                    CleanedFiles: [],
                    CleanedDirectories: []
                );
            }

            File.WriteAllText(manifest, patched);
            return new(
                ChangedFiles: [manifest],
                FollowUpCommand: followUpCommand,
                UnsupportedReason: null,
                CleanedFiles: lockfiles,
                CleanedDirectories: installedDirs
            );
        }

        return new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: $"Package '{packageName}' not found in {Path.GetFileName(manifest)}.",
            CleanedFiles: [],
            CleanedDirectories: []
        );
    }

    // Surgically inserts or updates a single entry in the "overrides" block without
    // deserialising and re-serialising the whole file, so the diff is bounded to the
    // inserted/updated lines only rather than reformatting every key in the document.
    //
    // Strategy:
    //   - If an "overrides" key already exists: find the opening brace of its value object,
    //     then either update the matching entry line in-place (regex replace) or insert a
    //     new entry before the closing brace of that object.
    //   - If no "overrides" key exists: find the closing brace of the root object, walk
    //     back past trailing whitespace/newlines, and insert a comma + "overrides" block
    //     before it — touching at most 1-2 lines of existing content.
    //
    // Returns null if the JSON cannot be parsed (malformed file).
    private static string? InjectOverride(string json, string packageName, string version)
    {
        // Validate that json parses before any text surgery.
        try
        {
            JsonDocument.Parse(json).Dispose();
        }
        catch (JsonException)
        {
            return null;
        }

        string pinned = $"^{version}";
        string escapedPackageName = JsonEncode(packageName);
        string escapedPinned = JsonEncode(pinned);

        // Try to find an existing "overrides" block.
        Regex overridesKeyPattern = new(
            @"""overrides""\s*:\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        Match overridesMatch = overridesKeyPattern.Match(json);

        if (overridesMatch.Success)
        {
            // overrides block exists — look for the package entry inside it.
            int blockStart = overridesMatch.Index + overridesMatch.Length;
            Regex entryPattern = new(
                $@"(""{Regex.Escape(packageName)}""\s*:\s*)""[^""]*""",
                RegexOptions.Compiled
            );
            Match entryMatch = entryPattern.Match(json, blockStart);

            // Only update the entry if it's inside the overrides block (before its closing brace).
            int closingBrace = FindMatchingBrace(json, blockStart - 1);
            if (entryMatch.Success && entryMatch.Index < closingBrace)
            {
                string updated =
                    json[..entryMatch.Index]
                    + entryMatch.Groups[1].Value
                    + $"{escapedPinned}"
                    + json[(entryMatch.Index + entryMatch.Length)..];
                return updated;
            }

            // Entry not in overrides yet — insert before the closing brace of the block.
            // Detect the indent used inside the overrides block by looking at the first
            // property line inside it.
            string indent = DetectIndent(json, blockStart);

            string blockContent = json[blockStart..closingBrace].Trim();
            bool hasEntries = blockContent.Length > 0;

            if (hasEntries)
            {
                // Find the last non-whitespace character before the closing brace so we can
                // append a trailing comma to the previous entry on its own line, then add the
                // new entry. This keeps the diff bounded: one line gains a comma, one line is
                // added, the closing-brace line is unchanged.
                int lastContentPos = closingBrace - 1;
                while (lastContentPos >= blockStart && char.IsWhiteSpace(json[lastContentPos]))
                    lastContentPos--;

                // lastContentPos now points at the last content character before the brace.
                // If it's already a comma the block is formatted inconsistently; add without comma.
                string trailingComma = json[lastContentPos] == ',' ? string.Empty : ",";
                string newEntry = $"\n{indent}{escapedPackageName}: {escapedPinned}";

                return json[..(lastContentPos + 1)]
                    + trailingComma
                    + newEntry
                    + "\n"
                    + json[closingBrace..];
            }
            else
            {
                // Empty block — no preceding entry to comma-terminate.
                string newEntry = $"\n{indent}{escapedPackageName}: {escapedPinned}\n";
                return json[..closingBrace] + newEntry + json[closingBrace..];
            }
        }

        // No "overrides" block — insert one before the root closing brace.
        int rootClose = json.LastIndexOf('}');
        if (rootClose < 0)
            return null;

        // Detect the root indentation level (2 or 4 spaces) from existing content.
        string rootIndent = DetectRootIndent(json);
        string innerIndent = rootIndent + rootIndent;

        // Walk back to find the last non-whitespace character before the root closing brace.
        // Append a trailing comma to that line, then insert the full overrides block.
        int rootLastContent = rootClose - 1;
        while (rootLastContent >= 0 && char.IsWhiteSpace(json[rootLastContent]))
            rootLastContent--;

        string rootComma =
            rootLastContent >= 0 && json[rootLastContent] != '{' && json[rootLastContent] != ','
                ? ","
                : string.Empty;

        string overridesBlock =
            $"\n{rootIndent}\"overrides\": {{\n{innerIndent}{escapedPackageName}: {escapedPinned}\n{rootIndent}}}";

        return json[..(rootLastContent + 1)]
            + rootComma
            + overridesBlock
            + "\n"
            + json[rootClose..];
    }

    // Finds the index of the matching closing brace for an opening brace at `openIndex`.
    private static int FindMatchingBrace(string json, int openIndex)
    {
        int depth = 0;
        for (int index = openIndex; index < json.Length; index++)
        {
            char ch = json[index];
            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }
        return json.Length - 1;
    }

    // Returns the indentation string used for property lines inside a JSON object
    // block starting at `blockStart` (the character after the opening brace).
    private static string DetectIndent(string json, int blockStart)
    {
        int nlPos = json.IndexOf('\n', blockStart);
        if (nlPos < 0)
            return "    ";
        int spaces = 0;
        for (int index = nlPos + 1; index < json.Length; index++)
        {
            if (json[index] == ' ')
                spaces++;
            else
                break;
        }
        return spaces > 0 ? new(' ', spaces) : "  ";
    }

    // Detects the root-level indentation (used for top-level keys like "dependencies").
    private static string DetectRootIndent(string json)
    {
        Regex topKeyPattern = new(@"^\s+""", RegexOptions.Multiline);
        Match match = topKeyPattern.Match(json);
        if (!match.Success)
            return "  ";
        string ws = match.Value[..^1];
        return ws.Length > 0 ? ws : "  ";
    }

    // JSON-encodes a string value (adds surrounding quotes, escapes special chars).
    // Used to safely embed package names and version strings in the edited file.
    private static string JsonEncode(string value)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
            writer.WriteStringValue(value);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // Determines the most appropriate install command based on which lockfile exists.
    private static string PickInstallCommand(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "pnpm-lock.yaml")))
            return "pnpm install";
        if (File.Exists(Path.Combine(rootPath, "yarn.lock")))
            return "yarn install";
        return "npm install";
    }

    private static IReadOnlyList<string> CollectLockfiles(string rootPath)
    {
        string[] candidates =
        [
            Path.Combine(rootPath, "package-lock.json"),
            Path.Combine(rootPath, "yarn.lock"),
            Path.Combine(rootPath, "pnpm-lock.yaml"),
            Path.Combine(rootPath, "npm-shrinkwrap.json"),
        ];
        return candidates.Where(File.Exists).ToArray();
    }

    private static IReadOnlyList<string> CollectInstalledDirs(string rootPath)
    {
        string nodeModules = Path.Combine(rootPath, "node_modules");
        return Directory.Exists(nodeModules) ? [nodeModules] : [];
    }
}
