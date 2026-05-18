using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Dart.Tests;

public class PubspecLockParserTests
{
    [Fact]
    public async Task ParseAsync_PubspecLock_ReturnsPackagesWithDirectFlag()
    {
        PubspecLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "pubspec.lock"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "pubspec.lock",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Pub);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "http" && item.Version == "1.2.0" && item.IsDirect);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "test" && item.Version == "1.25.0" && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "meta" && item.Version == "1.11.0" && !item.IsDirect
            );
    }
}
