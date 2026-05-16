using System.Text.RegularExpressions;
using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

public sealed class ComposerManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Composer;

    public ManifestEditOutcome Apply(string rootPath, string packageName, string suggestedVersion)
    {
        string manifest = Path.Combine(rootPath, "composer.json");
        if (!File.Exists(manifest))
        {
            return new ManifestEditOutcome(
                ChangedFiles: Array.Empty<string>(),
                FollowUpCommand: null,
                UnsupportedReason: $"No composer.json at source root ({rootPath})."
            );
        }

        string source = File.ReadAllText(manifest);
        string escaped = Regex.Escape(packageName);
        Regex pattern = new(
            $"(\"{escaped}\"\\s*:\\s*\")([^\"]+)(\")",
            RegexOptions.Compiled
        );

        Match match = pattern.Match(source);
        if (!match.Success)
        {
            return new ManifestEditOutcome(
                ChangedFiles: Array.Empty<string>(),
                FollowUpCommand: null,
                UnsupportedReason: $"Package '{packageName}' not found in composer.json."
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
            FollowUpCommand: "composer update",
            UnsupportedReason: null
        );
    }
}
