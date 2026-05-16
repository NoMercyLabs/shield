using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// Cargo.toml supports many version forms (string, table, workspace inherit). Phase 1
// surfaces as not-yet-supported and points the operator at `cargo update`.
public sealed class RustManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Rust;

    public ManifestEditOutcome Apply(
        string rootPath,
        string packageName,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: Array.Empty<string>(),
            FollowUpCommand: $"cargo update -p {packageName} --precise {suggestedVersion}",
            UnsupportedReason: "Rust manifest editing is not yet supported; run the follow-up command manually."
        );
}
