using System.Text.RegularExpressions;

namespace Shield.Api.Services.ManifestEditors;

// Best-effort: rewrites `group:name:version` coordinates in *.gradle / *.gradle.kts.
// Variable-substituted versions (e.g. `versions.lib`) fall through with a clear reason.
public sealed class GradleManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Gradle;

    public ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion)
    {
        string packageName = item.Name;
        int colon = packageName.LastIndexOf(':');
        if (colon <= 0)
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: "Gradle package names are expected as 'group:artifact'.",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        string coords = $"{Regex.Escape(packageName)}:";
        Regex pattern = new($"({coords})([\\w\\d.\\-+]+)", RegexOptions.Compiled);

        List<string> changed = [];
        foreach (string candidate in EnumerateBuildFiles(rootPath))
        {
            string source = File.ReadAllText(candidate);
            string updated = pattern.Replace(source, m => $"{m.Groups[1].Value}{suggestedVersion}");
            if (!string.Equals(updated, source, StringComparison.Ordinal))
            {
                File.WriteAllText(candidate, updated);
                changed.Add(candidate);
            }
        }

        if (changed.Count == 0)
        {
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: $"No literal '{packageName}' coordinate found; may be variable-substituted (not yet supported).",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        return new(
            ChangedFiles: changed,
            FollowUpCommand: "./gradlew dependencies --refresh-dependencies",
            UnsupportedReason: null,
            CleanedFiles: [],
            CleanedDirectories: []
        );
    }

    private static IEnumerable<string> EnumerateBuildFiles(string root)
    {
        foreach (string pattern in new[] { "*.gradle", "*.gradle.kts" })
        {
            IEnumerable<string> hits;
            try
            {
                hits = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string file in hits)
                yield return file;
        }
    }
}
