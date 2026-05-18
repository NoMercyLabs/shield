namespace Shield.Api.Services.ManifestEditors;

// Cargo.toml supports many version forms (string, table, workspace inherit). Phase 1
// surfaces as not-yet-supported and points the operator at `cargo update`.
public sealed class RustManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Rust;

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: $"cargo update -p {item.Name} --precise {suggestedVersion}",
            UnsupportedReason: "Rust manifest editing is not yet supported; run the follow-up command manually.",
            CleanedFiles: [],
            CleanedDirectories: []
        );
}
