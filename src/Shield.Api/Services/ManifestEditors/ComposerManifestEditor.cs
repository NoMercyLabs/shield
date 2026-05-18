using System.Text.RegularExpressions;

namespace Shield.Api.Services.ManifestEditors;

public sealed class ComposerManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Composer;

    public ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion)
    {
        string packageName = item.Name;

        // Composer has no overrides equivalent for transitive deps.
        if (!item.IsDirect)
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: "Transitive dependency — no Composer equivalent of npm overrides. Bump manually or add the package as a direct require.",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        string manifestRelative = !string.IsNullOrWhiteSpace(item.ManifestPath)
            ? item.ManifestPath.Replace('/', Path.DirectorySeparatorChar)
            : "composer.json";
        string manifest = Path.Combine(rootPath, manifestRelative);
        if (!File.Exists(manifest))
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: $"No composer.json at source root ({rootPath}).",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        string source = File.ReadAllText(manifest);
        string escaped = Regex.Escape(packageName);
        Regex pattern = new($"(\"{escaped}\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled);

        Match match = pattern.Match(source);
        if (!match.Success)
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: $"Package '{packageName}' not found in composer.json.",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        string replacement = $"{match.Groups[1].Value}^{suggestedVersion}{match.Groups[3].Value}";
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

        string packageDir = Path.GetDirectoryName(manifest) ?? rootPath;
        string composerLock = Path.Combine(packageDir, "composer.lock");
        string vendorDir = Path.Combine(packageDir, "vendor");

        File.WriteAllText(manifest, updated);
        return new(
            ChangedFiles: [manifest],
            FollowUpCommand: "composer install",
            UnsupportedReason: null,
            CleanedFiles: File.Exists(composerLock) ? [composerLock] : [],
            CleanedDirectories: Directory.Exists(vendorDir) ? [vendorDir] : []
        );
    }
}
