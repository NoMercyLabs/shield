using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Parsers.Composer;
using Shield.Parsers.Dart;
using Shield.Parsers.Elixir;
using Shield.Parsers.Go;
using Shield.Parsers.Gradle;
using Shield.Parsers.Maven;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Parsers.Python;
using Shield.Parsers.Ruby;
using Shield.Parsers.Rust;
using Shield.Parsers.Swift;
using Shield.Parsers.Vcpkg;

namespace Shield.Scanners;

public sealed class ParserRegistry
{
    // Lockfile = pinned, resolved set produced by a package manager. Manifest = declared
    // dependency list authored by the developer. Some ecosystems make us parse both —
    // requirements.txt and go.mod aren't true lockfiles but carry pinned/version data we use
    // as fallbacks when no lock is present.
    public enum FilenameRole
    {
        Lockfile,
        Manifest,
    }

    private static readonly IReadOnlyDictionary<
        string,
        (Ecosystem Ecosystem, FilenameRole Role)
    > FilenameToEntry = new Dictionary<string, (Ecosystem, FilenameRole)>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["package-lock.json"] = (Ecosystem.Npm, FilenameRole.Lockfile),
        ["npm-shrinkwrap.json"] = (Ecosystem.Npm, FilenameRole.Lockfile),
        ["yarn.lock"] = (Ecosystem.Npm, FilenameRole.Lockfile),
        ["pnpm-lock.yaml"] = (Ecosystem.Npm, FilenameRole.Lockfile),
        ["packages.lock.json"] = (Ecosystem.Nuget, FilenameRole.Lockfile),
        ["Directory.Packages.props"] = (Ecosystem.Nuget, FilenameRole.Manifest),
        ["composer.lock"] = (Ecosystem.Composer, FilenameRole.Lockfile),
        ["gradle.lockfile"] = (Ecosystem.Gradle, FilenameRole.Lockfile),
        ["poetry.lock"] = (Ecosystem.Python, FilenameRole.Lockfile),
        ["pdm.lock"] = (Ecosystem.Python, FilenameRole.Lockfile),
        ["uv.lock"] = (Ecosystem.Python, FilenameRole.Lockfile),
        ["Pipfile.lock"] = (Ecosystem.Python, FilenameRole.Lockfile),
        ["requirements.txt"] = (Ecosystem.Python, FilenameRole.Manifest),
        ["go.sum"] = (Ecosystem.Go, FilenameRole.Lockfile),
        ["go.mod"] = (Ecosystem.Go, FilenameRole.Manifest),
        ["Cargo.lock"] = (Ecosystem.Rust, FilenameRole.Lockfile),
        ["Cargo.toml"] = (Ecosystem.Rust, FilenameRole.Manifest),
        ["Gemfile.lock"] = (Ecosystem.RubyGems, FilenameRole.Lockfile),
        ["Package.resolved"] = (Ecosystem.SwiftPM, FilenameRole.Lockfile),
        ["pubspec.lock"] = (Ecosystem.Pub, FilenameRole.Lockfile),
        ["pom.xml"] = (Ecosystem.Maven, FilenameRole.Manifest),
        ["mix.lock"] = (Ecosystem.Hex, FilenameRole.Lockfile),
        ["vcpkg.json"] = (Ecosystem.Vcpkg, FilenameRole.Manifest),
    };

    private readonly NpmLockParser _npm;
    private readonly NugetDependencyParser _nuget;
    private readonly ComposerLockParser _composer;
    private readonly GradleLockfileParser _gradle;
    private readonly PythonDependencyParser _python;
    private readonly GoDependencyParser _go;
    private readonly RustDependencyParser _rust;
    private readonly GemfileLockParser _ruby;
    private readonly PackageResolvedParser _swift;
    private readonly PubspecLockParser _dart;
    private readonly PomXmlParser _maven;
    private readonly MixLockParser _elixir;
    private readonly VcpkgJsonParser _vcpkg;

    public ParserRegistry(
        NpmLockParser npm,
        NugetDependencyParser nuget,
        ComposerLockParser composer,
        GradleLockfileParser gradle,
        PythonDependencyParser python,
        GoDependencyParser go,
        RustDependencyParser rust,
        GemfileLockParser ruby,
        PackageResolvedParser swift,
        PubspecLockParser dart,
        PomXmlParser maven,
        MixLockParser elixir,
        VcpkgJsonParser vcpkg
    )
    {
        _npm = npm;
        _nuget = nuget;
        _composer = composer;
        _gradle = gradle;
        _python = python;
        _go = go;
        _rust = rust;
        _ruby = ruby;
        _swift = swift;
        _dart = dart;
        _maven = maven;
        _elixir = elixir;
        _vcpkg = vcpkg;
    }

    public static bool IsRecognized(string filename) =>
        FilenameToEntry.ContainsKey(Path.GetFileName(filename))
        || IsCentralPackagesProps(Path.GetFileName(filename));

    public static IReadOnlyCollection<string> RecognizedFilenames =>
        (IReadOnlyCollection<string>)FilenameToEntry.Keys;

    public static IReadOnlyCollection<string> LockfileFilenames { get; } =
        FilenameToEntry
            .Where(kvp => kvp.Value.Role == FilenameRole.Lockfile)
            .Select(kvp => kvp.Key)
            .ToArray();

    public static IReadOnlyCollection<string> ManifestFilenames { get; } =
        FilenameToEntry
            .Where(kvp => kvp.Value.Role == FilenameRole.Manifest)
            .Select(kvp => kvp.Key)
            .ToArray();

    public static FilenameRole? RoleFor(string filename) =>
        FilenameToEntry.TryGetValue(Path.GetFileName(filename), out var entry) ? entry.Role : null;

    public IParser? FindFor(string filename)
    {
        string name = Path.GetFileName(filename);
        if (!FilenameToEntry.TryGetValue(name, out var entry))
        {
            // Env-variant props files like Directory.Packages.Production.props are not in the
            // static dict (too many combinations). Route them by pattern instead.
            if (IsCentralPackagesProps(name))
                return _nuget;
            return null;
        }

        return entry.Ecosystem switch
        {
            Ecosystem.Npm => _npm,
            Ecosystem.Nuget => _nuget,
            Ecosystem.Composer => _composer,
            Ecosystem.Gradle => _gradle,
            Ecosystem.Python => _python,
            Ecosystem.Go => _go,
            Ecosystem.Rust => _rust,
            Ecosystem.RubyGems => _ruby,
            Ecosystem.SwiftPM => _swift,
            Ecosystem.Pub => _dart,
            Ecosystem.Maven => _maven,
            Ecosystem.Hex => _elixir,
            Ecosystem.Vcpkg => _vcpkg,
            _ => null,
        };
    }

    // Directory.Packages.props and Directory.Packages.<env>.props (any casing).
    private static bool IsCentralPackagesProps(string name)
    {
        if (!name.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            return false;
        string stem = name[..^".props".Length];
        if (stem.Equals("Directory.Packages", StringComparison.OrdinalIgnoreCase))
            return true;
        return stem.StartsWith("Directory.Packages.", StringComparison.OrdinalIgnoreCase)
            && stem.Length > "Directory.Packages.".Length;
    }
}
