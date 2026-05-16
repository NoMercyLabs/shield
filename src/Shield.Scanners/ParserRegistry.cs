using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Parsers.Composer;
using Shield.Parsers.Go;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Parsers.Python;
using Shield.Parsers.Rust;

namespace Shield.Scanners;

public sealed class ParserRegistry
{
    static readonly IReadOnlyDictionary<string, Ecosystem> FilenameToEcosystem = new Dictionary<
        string,
        Ecosystem
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["package-lock.json"] = Ecosystem.Npm,
        ["npm-shrinkwrap.json"] = Ecosystem.Npm,
        ["yarn.lock"] = Ecosystem.Npm,
        ["pnpm-lock.yaml"] = Ecosystem.Npm,
        ["packages.lock.json"] = Ecosystem.Nuget,
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
    };

    readonly NpmLockParser _npm;
    readonly NugetLockParser _nuget;
    readonly ComposerLockParser _composer;
    readonly GradleLockfileParser _gradle;
    readonly PythonLockParser _python;
    readonly GoLockParser _go;
    readonly RustLockParser _rust;

    public ParserRegistry(
        NpmLockParser npm,
        NugetLockParser nuget,
        ComposerLockParser composer,
        GradleLockfileParser gradle,
        PythonLockParser python,
        GoLockParser go,
        RustLockParser rust
    )
    {
        _npm = npm;
        _nuget = nuget;
        _composer = composer;
        _gradle = gradle;
        _python = python;
        _go = go;
        _rust = rust;
    }

    public static bool IsRecognized(string filename) =>
        FilenameToEcosystem.ContainsKey(Path.GetFileName(filename));

    public static IReadOnlyCollection<string> RecognizedFilenames =>
        (IReadOnlyCollection<string>)FilenameToEcosystem.Keys;

    public IParser? FindFor(string filename)
    {
        string name = Path.GetFileName(filename);
        if (!FilenameToEcosystem.TryGetValue(name, out Ecosystem ecosystem))
            return null;

        return ecosystem switch
        {
            Ecosystem.Npm => _npm,
            Ecosystem.Nuget => _nuget,
            Ecosystem.Composer => _composer,
            Ecosystem.Gradle => _gradle,
            Ecosystem.Python => _python,
            Ecosystem.Go => _go,
            Ecosystem.Rust => _rust,
            _ => null,
        };
    }
}
