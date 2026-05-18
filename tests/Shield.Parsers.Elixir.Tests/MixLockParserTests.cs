using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Elixir.Tests;

public class MixLockParserTests
{
    [Fact]
    public async Task ParseAsync_MixLock_ReturnsHexPackagesWithVersions()
    {
        MixLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "mix.lock"));

        ParseResult result = await parser.ParseAsync(stream, "mix.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Hex);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "phoenix" && item.Version == "1.7.10");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "jason" && item.Version == "1.4.1");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "decimal" && item.Version == "2.1.1");
    }
}
