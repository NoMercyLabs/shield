using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Parsers.Composer;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;

namespace Shield.Scanners;

public sealed class ParserRegistry
{
    static readonly IReadOnlyDictionary<string, Ecosystem> FilenameToEcosystem =
        new Dictionary<string, Ecosystem>(StringComparer.OrdinalIgnoreCase)
        {
            ["package-lock.json"] = Ecosystem.Npm,
            ["npm-shrinkwrap.json"] = Ecosystem.Npm,
            ["yarn.lock"] = Ecosystem.Npm,
            ["pnpm-lock.yaml"] = Ecosystem.Npm,
            ["packages.lock.json"] = Ecosystem.Nuget,
            ["composer.lock"] = Ecosystem.Composer,
            ["gradle.lockfile"] = Ecosystem.Gradle,
        };

    readonly NpmLockParser _npm;
    readonly NugetLockParser _nuget;
    readonly ComposerLockParser _composer;
    readonly GradleLockfileParser _gradle;

    public ParserRegistry(
        NpmLockParser npm,
        NugetLockParser nuget,
        ComposerLockParser composer,
        GradleLockfileParser gradle
    )
    {
        _npm = npm;
        _nuget = nuget;
        _composer = composer;
        _gradle = gradle;
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
            _ => null,
        };
    }
}
