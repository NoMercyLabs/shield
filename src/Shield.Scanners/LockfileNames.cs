namespace Shield.Scanners;

// Filename-only lockfile recognizer. Mirrors ParserRegistry's recognized filenames so the
// FS browser can flag interesting directories without instantiating parsers.
public static class LockfileNames
{
    static readonly HashSet<string> Set = new(
        ParserRegistry.RecognizedFilenames,
        StringComparer.OrdinalIgnoreCase
    );

    // Adjacent manifests we surface in the browser for context (not parsed, not counted).
    static readonly HashSet<string> ContextSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json",
        "Cargo.toml",
        "composer.json",
        "pyproject.toml",
    };

    public static IReadOnlyCollection<string> All => Set;

    public static IReadOnlyCollection<string> Context => ContextSet;

    public static bool IsLockfile(string filename) => Set.Contains(Path.GetFileName(filename));

    public static bool IsContextManifest(string filename) =>
        ContextSet.Contains(Path.GetFileName(filename));
}
