using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// Python manifests live in many shapes (requirements.txt, pyproject.toml [tool.poetry],
// Pipfile, setup.cfg). Phase 1 ships as not-yet-supported until we settle on one.
public sealed class PythonManifestEditor : IManifestEditor
{
    public Ecosystem Ecosystem => Ecosystem.Python;

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Python manifest editing is not yet supported in this build.",
            CleanedFiles: [],
            CleanedDirectories: []
        );
}
