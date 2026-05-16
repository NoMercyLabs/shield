using Shield.Core.Domain;

namespace Shield.Api.Services.ManifestEditors;

// Per-ecosystem manifest patcher. Implementations enumerate candidate manifest files in
// `rootPath`, edit the dep version in place, and report what changed. Returning an empty
// list means "no matching manifest found / not yet supported for this case" — callers map
// that into a 400 so the UI can surface a clear "not yet supported" reason.
public sealed record ManifestEditOutcome(
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? UnsupportedReason
);

public interface IManifestEditor
{
    Ecosystem Ecosystem { get; }

    // `rootPath` is the LocalFolder source root. `packageName` + `suggestedVersion`
    // are advisory-driven. Implementations may write to multiple files (e.g. csproj + lock).
    ManifestEditOutcome Apply(string rootPath, string packageName, string suggestedVersion);
}
