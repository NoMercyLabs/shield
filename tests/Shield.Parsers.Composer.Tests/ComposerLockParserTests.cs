using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Composer.Tests;

public class ComposerLockParserTests
{
    [Fact]
    public async Task ParseAsync_ComposerLock_ReturnsPackagesAndDevPackages()
    {
        ComposerLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "composer.lock"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "composer.lock",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Diagnostics.Should().BeEmpty();
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Composer);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "monolog/monolog" && item.Version == "3.5.0");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "psr/log" && item.Version == "3.0.0");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "phpunit/phpunit" && item.Version == "10.5.0");
    }

    [Fact]
    public async Task ParseAsync_ComposerLock_AllItemsDefaultIsDirectTrue()
    {
        ComposerLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "composer.lock"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "composer.lock",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        // composer.lock doesn't expose direct/transitive in isolation — scanner
        // defaults all entries to IsDirect=true per migration convention.
        result.Items.Should().OnlyContain(item => item.IsDirect);
    }
}
