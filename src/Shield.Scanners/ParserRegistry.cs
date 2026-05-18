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
    private static readonly IReadOnlyDictionary<string, Ecosystem> FilenameToEcosystem =
        new Dictionary<string, Ecosystem>(StringComparer.OrdinalIgnoreCase)
        {
            ["package-lock.json"] = Ecosystem.Npm,
            ["npm-shrinkwrap.json"] = Ecosystem.Npm,
            ["yarn.lock"] = Ecosystem.Npm,
            ["pnpm-lock.yaml"] = Ecosystem.Npm,
            ["packages.lock.json"] = Ecosystem.Nuget,
            ["Directory.Packages.props"] = Ecosystem.Nuget,
            ["composer.lock"] = Ecosystem.Composer,
            ["gradle.lockfile"] = Ecosystem.Gradle,
            ["poetry.lock"] = Ecosystem.Python,
            ["pdm.lock"] = Ecosystem.Python,
            ["uv.lock"] = Ecosystem.Python,
            ["Pipfile.lock"] = Ecosystem.Python,
            ["requirements.txt"] = Ecosystem.Python,
            ["go.sum"] = Ecosystem.Go,
            ["go.mod"] = Ecosystem.Go,
            ["Cargo.lock"] = Ecosystem.Rust,
            ["Cargo.toml"] = Ecosystem.Rust,
            ["Gemfile.lock"] = Ecosystem.RubyGems,
            ["Package.resolved"] = Ecosystem.SwiftPM,
            ["pubspec.lock"] = Ecosystem.Pub,
            ["pom.xml"] = Ecosystem.Maven,
            ["mix.lock"] = Ecosystem.Hex,
            ["vcpkg.json"] = Ecosystem.Vcpkg,
        };

    private readonly NpmLockParser _npm;
    private readonly NugetLockParser _nuget;
    private readonly ComposerLockParser _composer;
    private readonly GradleLockfileParser _gradle;
    private readonly PythonLockParser _python;
    private readonly GoLockParser _go;
    private readonly RustLockParser _rust;
    private readonly GemfileLockParser _ruby;
    private readonly PackageResolvedParser _swift;
    private readonly PubspecLockParser _dart;
    private readonly PomXmlParser _maven;
    private readonly MixLockParser _elixir;
    private readonly VcpkgJsonParser _vcpkg;

    public ParserRegistry(
        NpmLockParser npm,
        NugetLockParser nuget,
        ComposerLockParser composer,
        GradleLockfileParser gradle,
        PythonLockParser python,
        GoLockParser go,
        RustLockParser rust,
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
        FilenameToEcosystem.ContainsKey(Path.GetFileName(filename))
        || IsCentralPackagesProps(Path.GetFileName(filename));

    public static IReadOnlyCollection<string> RecognizedFilenames =>
        (IReadOnlyCollection<string>)FilenameToEcosystem.Keys;

    public IParser? FindFor(string filename)
    {
        string name = Path.GetFileName(filename);
        if (!FilenameToEcosystem.TryGetValue(name, out Ecosystem ecosystem))
        {
            // Env-variant props files like Directory.Packages.Production.props are not in the
            // static dict (too many combinations). Route them by pattern instead.
            if (IsCentralPackagesProps(name))
                return _nuget;
            return null;
        }

        return ecosystem switch
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
