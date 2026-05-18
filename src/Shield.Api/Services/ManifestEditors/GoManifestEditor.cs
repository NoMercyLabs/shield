using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// go.mod edits need to preserve module-graph invariants; safer to surface as not-yet-supported
// and have the operator run `go get module@version` themselves.
public sealed class GoManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Go;

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: $"go get {item.Name}@v{suggestedVersion}",
            UnsupportedReason: "Go manifest editing is not yet supported; run the follow-up command manually.",
            CleanedFiles: [],
            CleanedDirectories: []
        );
}
