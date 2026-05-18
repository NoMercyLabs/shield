namespace Shield.Scanners;

// Filename-only recognizer used by the FS browser to flag interesting files without
// instantiating parsers. The split matches ParserRegistry's role tagging:
//   - Lockfile: pinned, resolved (package-lock.json, Cargo.lock, etc.)
//   - Manifest: declared (pom.xml, Cargo.toml, go.mod, requirements.txt, vcpkg.json, ...)
// A file is one or the other — never both.
public static class ParserFilenames
{
    private static readonly HashSet<string> LockfileSet = new(
        ParserRegistry.LockfileFilenames,
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly HashSet<string> ManifestSet = new(
        ParserRegistry.ManifestFilenames,
        StringComparer.OrdinalIgnoreCase
    );

    public static IReadOnlyCollection<string> Lockfiles => LockfileSet;

    public static IReadOnlyCollection<string> Manifests => ManifestSet;

    public static bool IsLockfile(string filename) =>
        LockfileSet.Contains(Path.GetFileName(filename));

    public static bool IsManifest(string filename) =>
        ManifestSet.Contains(Path.GetFileName(filename));

    public static bool IsRecognized(string filename) =>
        IsLockfile(filename) || IsManifest(filename);
}
