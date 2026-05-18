using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Go.Tests;

public class GoSumParserTests
{
    [Fact]
    public async Task ParseAsync_GoSum_DedupesPerPair()
    {
        GoLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "go.sum"));

        ParseResult result = await parser.ParseAsync(stream, "go.sum", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Go);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "github.com/gin-gonic/gin" && item.Version == "v1.9.1"
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "github.com/stretchr/testify" && item.Version == "v1.8.4"
            );
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "golang.org/x/sys" && item.Version == "v0.15.0");
    }

    [Fact]
    public async Task ParseAsync_GoSum_AllItemsAreTransitive()
    {
        GoLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "go.sum"));

        ParseResult result = await parser.ParseAsync(stream, "go.sum", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        // go.sum has no direct/transitive distinction — all entries default IsDirect=false.
        // Direct deps come from go.mod with IsDirect=true.
        result.Items.Should().OnlyContain(item => !item.IsDirect);
    }
}
