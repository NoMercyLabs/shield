using FluentAssertions;
using Shield.Core.Abstractions;
using Shield.Parsers.Composer;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Scanners;
using Xunit;

namespace Shield.Scanners.Tests;

public class ParserRegistryTests
{
    static ParserRegistry NewRegistry() =>
        new(new NpmLockParser(), new NugetLockParser(), new ComposerLockParser(), new GradleLockfileParser());

    [Theory]
    [InlineData("package-lock.json", typeof(NpmLockParser))]
    [InlineData("npm-shrinkwrap.json", typeof(NpmLockParser))]
    [InlineData("yarn.lock", typeof(NpmLockParser))]
    [InlineData("pnpm-lock.yaml", typeof(NpmLockParser))]
    [InlineData("packages.lock.json", typeof(NugetLockParser))]
    [InlineData("composer.lock", typeof(ComposerLockParser))]
    [InlineData("gradle.lockfile", typeof(GradleLockfileParser))]
    public void FindFor_routes_filenames_to_correct_parser(string filename, Type expected)
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor(filename);

        parser.Should().NotBeNull();
        parser!.GetType().Should().Be(expected);
    }

    [Fact]
    public void FindFor_strips_directory_components()
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor("apps/web/package-lock.json");

        parser.Should().BeOfType<NpmLockParser>();
    }

    [Fact]
    public void FindFor_returns_null_for_unknown_filename()
    {
        ParserRegistry registry = NewRegistry();

        IParser? parser = registry.FindFor("README.md");

        parser.Should().BeNull();
    }

    [Fact]
    public void IsRecognized_matches_known_filenames_case_insensitively()
    {
        ParserRegistry.IsRecognized("package-lock.json").Should().BeTrue();
        ParserRegistry.IsRecognized("Composer.Lock").Should().BeTrue();
        ParserRegistry.IsRecognized("random.txt").Should().BeFalse();
    }
}
