using System.Text.RegularExpressions;
using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// Bumps a dep's `^x.y.z` style version in package.json. Uses a targeted regex so
// formatting/comments around other entries are untouched. Does not run `npm install` —
// the response carries a `followUpCommand` hint so the operator can rebuild locally.
public sealed class NpmManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Npm;

    public ManifestEditOutcome Apply(string rootPath, string packageName, string suggestedVersion)
    {
        string manifest = Path.Combine(rootPath, "package.json");
        if (!File.Exists(manifest))
        {
            return new ManifestEditOutcome(
                ChangedFiles: Array.Empty<string>(),
                FollowUpCommand: null,
                UnsupportedReason: $"No package.json at source root ({rootPath})."
            );
        }

        string source = File.ReadAllText(manifest);
        string escaped = Regex.Escape(packageName);
        // Match the dependency line inside any deps block — capture the JSON key/value
        // pair so we can rewrite just the version literal.
        Regex pattern = new($"(\"{escaped}\"\\s*:\\s*\")([^\"]+)(\")", RegexOptions.Compiled);

        Match match = pattern.Match(source);
        if (!match.Success)
        {
            return new ManifestEditOutcome(
                ChangedFiles: Array.Empty<string>(),
                FollowUpCommand: null,
                UnsupportedReason: $"Package '{packageName}' not found in {Path.GetFileName(manifest)}."
            );
        }

        string replacement = $"{match.Groups[1].Value}^{suggestedVersion}{match.Groups[3].Value}";
        string updated = pattern.Replace(source, replacement, count: 1);

        if (string.Equals(updated, source, StringComparison.Ordinal))
        {
            return new ManifestEditOutcome(
                ChangedFiles: Array.Empty<string>(),
                FollowUpCommand: null,
                UnsupportedReason: "Already at suggested version."
            );
        }

        File.WriteAllText(manifest, updated);
        return new ManifestEditOutcome(
            ChangedFiles: new[] { manifest },
            FollowUpCommand: "npm install",
            UnsupportedReason: null
        );
    }
}
