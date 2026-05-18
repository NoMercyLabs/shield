namespace Shield.Scanners;

// Directory names skipped by both LocalFolderScanner + GitHubRepoScanner unless the source
// config explicitly overrides via ignoreGlobs. Matched per path-segment (case-insensitive)
// so a file at `tests/Foo.Tests/Fixtures/composer.lock` is dropped because one of its
// segments is `Fixtures` — the same lockfile under `apps/web/composer.lock` is kept.
//
// Why these specific names: empirically these are the only path segments that show up as
// noise in well-organised projects. Adding `tests/` itself would nuke legitimate test deps;
// adding `target/` would nuke Rust deps users actually want audited. The set below covers
// the false-positive class without false-negatives we've observed.
public static class ScannerIgnoreDefaults
{
    public static readonly IReadOnlyList<string> Segments =
    [
        // VCS + build outputs
        ".git",
        "node_modules",
        "vendor",
        "bin",
        "obj",
        // Test-fixture conventions across ecosystems
        "Fixtures",
        "fixtures",
        "__fixtures__",
        "test-data",
        "testdata",
        // Python virtualenv + cache
        "__pycache__",
        ".venv",
        "venv",
    ];

    // True when any segment of the given path matches one of the ignore names. Use this
    // from the GitHub scanner where files come as `tree/path/strings`; the local scanner
    // does its own subdir filtering during directory enumeration so it doesn't need this.
    public static bool ContainsIgnoredSegment(string treePath, HashSet<string> ignoreSet)
    {
        ReadOnlySpan<char> remaining = treePath.AsSpan();
        while (!remaining.IsEmpty)
        {
            int slash = remaining.IndexOf('/');
            ReadOnlySpan<char> segment = slash < 0 ? remaining : remaining[..slash];
            if (!segment.IsEmpty)
            {
                string candidate = segment.ToString();
                if (ignoreSet.Contains(candidate))
                    return true;
            }
            if (slash < 0)
                break;
            remaining = remaining[(slash + 1)..];
        }
        return false;
    }
}
