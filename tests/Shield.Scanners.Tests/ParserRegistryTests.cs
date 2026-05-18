using FluentAssertions;
using Shield.Core.Abstractions;
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
using Xunit;

namespace Shield.Scanners.Tests;

public class ParserRegistryTests
{
    private static ParserRegistry NewRegistry() =>
        new(
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new()
        );

    [Theory]
    [InlineData("package-lock.json", typeof(NpmLockParser))]
    [InlineData("npm-shrinkwrap.json", typeof(NpmLockParser))]
    [InlineData("yarn.lock", typeof(NpmLockParser))]
    [InlineData("pnpm-lock.yaml", typeof(NpmLockParser))]
    [InlineData("packages.lock.json", typeof(NugetDependencyParser))]
    [InlineData("composer.lock", typeof(ComposerLockParser))]
    [InlineData("gradle.lockfile", typeof(GradleLockfileParser))]
    [InlineData("poetry.lock", typeof(PythonDependencyParser))]
    [InlineData("Pipfile.lock", typeof(PythonDependencyParser))]
    [InlineData("requirements.txt", typeof(PythonDependencyParser))]
    [InlineData("go.sum", typeof(GoDependencyParser))]
    [InlineData("go.mod", typeof(GoDependencyParser))]
    [InlineData("Cargo.lock", typeof(RustDependencyParser))]
    [InlineData("Cargo.toml", typeof(RustDependencyParser))]
    [InlineData("Gemfile.lock", typeof(GemfileLockParser))]
    [InlineData("Package.resolved", typeof(PackageResolvedParser))]
    [InlineData("pubspec.lock", typeof(PubspecLockParser))]
    [InlineData("pom.xml", typeof(PomXmlParser))]
    [InlineData("mix.lock", typeof(MixLockParser))]
    [InlineData("vcpkg.json", typeof(VcpkgJsonParser))]
    public void FindForRoutesFilenamesToCorrectParser(string filename, Type expected)
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor(filename);

        parser.Should().NotBeNull();
        parser!.GetType().Should().Be(expected);
    }

    [Fact]
    public void FindForStripsDirectoryComponents()
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor("apps/web/package-lock.json");

        parser.Should().BeOfType<NpmLockParser>();
    }

    [Fact]
    public void FindForReturnsNullForUnknownFilename()
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor("README.md");

        parser.Should().BeNull();
    }

    [Fact]
    public void IsRecognizedMatchesKnownFilenamesCaseInsensitively()
    {
        ParserRegistry.IsRecognized("package-lock.json").Should().BeTrue();
        ParserRegistry.IsRecognized("Composer.Lock").Should().BeTrue();
        ParserRegistry.IsRecognized("random.txt").Should().BeFalse();
    }
}
