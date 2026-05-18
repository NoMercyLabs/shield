using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Gradle.Tests;

public class GradleLockfileParserTests
{
    [Fact]
    public async Task ParseAsyncGradleLockfileReturnsCoordinates()
    {
        GradleLockfileParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "gradle.lockfile"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "gradle.lockfile",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Diagnostics.Should().BeEmpty();
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Gradle);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "androidx.core:core-ktx" && item.Version == "1.13.1"
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "com.squareup.okhttp3:okhttp" && item.Version == "4.12.0"
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "org.jetbrains.kotlin:kotlin-stdlib" && item.Version == "2.0.0"
            );
    }

    [Fact]
    public async Task ParseAsyncBuildGradleKtsFallbackFlagsLockfileMissingAndExtractsImplementations()
    {
        GradleLockfileParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "build.gradle.kts"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "build.gradle.kts",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Diagnostics.Should().ContainKey("lockfileMissing");
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Gradle);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "junit:junit" && item.Version == "4.13.2");
    }
}
