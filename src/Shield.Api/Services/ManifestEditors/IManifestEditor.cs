namespace Shield.Api.Services.ManifestEditors;

// Per-ecosystem manifest patcher. Implementations enumerate candidate manifest files in
// `rootPath`, edit the dep version in place, and report what changed. Returning an empty
// list means "no matching manifest found / not yet supported for this case" — callers map
// that into a 400 so the UI can surface a clear "not yet supported" reason.
public sealed record ManifestEditOutcome(
    IReadOnlyList<string> ChangedFiles,
    string? FollowUpCommand,
    string? UnsupportedReason,
    IReadOnlyList<string> CleanedFiles,
    IReadOnlyList<string> CleanedDirectories
);

// Internal contract: IEcosystem is the public surface every other service injects. Each
// concrete editor class is still public (DI activates it by concrete type from IEcosystem
// implementations) but the shared shape stays private to this assembly.
internal interface IManifestEditor
{
    Ecosystem Ecosystem { get; }

    // `rootPath` is the LocalFolder source root. `item` carries the full inventory context
    // (package name, version, IsDirect) so editors can choose the right patching strategy
    // (direct-dep edit vs overrides injection). Implementations may write to multiple files.
    ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion);
}
