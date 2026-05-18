using System.Text.RegularExpressions;
using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// Rewrites <PackageReference Include="X" Version="Y" /> in every .csproj under root.
// Centrally-managed projects (Directory.Packages.props) get their pinned version updated.
public sealed class NugetManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Nuget;

    public ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion)
    {
        string packageName = item.Name;
        List<string> changed = [];
        string escaped = Regex.Escape(packageName);
        Regex packageReference = new(
            $"(<PackageReference\\s+Include=\"{escaped}\"[^/>]*Version=\")([^\"]+)(\")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        Regex packageVersion = new(
            $"(<PackageVersion\\s+Include=\"{escaped}\"[^/>]*Version=\")([^\"]+)(\")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        IEnumerable<string> candidates = !string.IsNullOrWhiteSpace(item.ManifestPath)
            ? new[]
            {
                Path.Combine(rootPath, item.ManifestPath.Replace('/', Path.DirectorySeparatorChar)),
            }
            : EnumerateManifests(rootPath);

        foreach (string candidate in candidates)
        {
            string source = File.ReadAllText(candidate);
            string updated = packageReference.Replace(
                source,
                m => $"{m.Groups[1].Value}{suggestedVersion}{m.Groups[3].Value}"
            );
            updated = packageVersion.Replace(
                updated,
                m => $"{m.Groups[1].Value}{suggestedVersion}{m.Groups[3].Value}"
            );

            if (!string.Equals(updated, source, StringComparison.Ordinal))
            {
                File.WriteAllText(candidate, updated);
                changed.Add(candidate);
            }
        }

        if (changed.Count == 0)
        {
            string location = !string.IsNullOrWhiteSpace(item.ManifestPath)
                ? item.ManifestPath
                : rootPath;
            return new(
                ChangedFiles: [],
                FollowUpCommand: null,
                UnsupportedReason: $"No <PackageReference> or <PackageVersion> for '{packageName}' found in {location}.",
                CleanedFiles: [],
                CleanedDirectories: []
            );
        }

        return new(
            ChangedFiles: changed,
            FollowUpCommand: "dotnet restore",
            UnsupportedReason: null,
            CleanedFiles: [],
            CleanedDirectories: []
        );
    }

    private static IEnumerable<string> EnumerateManifests(string root)
    {
        foreach (string pattern in new[] { "*.csproj", "Directory.Packages.props" })
        {
            foreach (string file in EnumerateSafely(root, pattern))
                yield return file;
        }
    }

    private static IEnumerable<string> EnumerateSafely(string root, string pattern)
    {
        IEnumerable<string> hits;
        try
        {
            hits = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (string file in hits)
        {
            string relative = Path.GetRelativePath(root, file);
            if (
                relative.Contains(
                    Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
                || relative.Contains(
                    Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
            )
                continue;
            yield return file;
        }
    }
}
