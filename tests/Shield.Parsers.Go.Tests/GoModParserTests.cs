using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Go.Tests;

public class GoModParserTests
{
    [Fact]
    public async Task ParseAsyncGoModMarksIndirectFlag()
    {
        GoDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "go.mod"));

        ParseResult result = await parser.ParseAsync(stream, "go.mod", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);

        InventoryItem gin = result.Items.Single(item => item.Name == "github.com/gin-gonic/gin");
        gin.Version.Should().Be("v1.9.1");
        gin.IsDirect.Should().BeTrue();

        InventoryItem sys = result.Items.Single(item => item.Name == "golang.org/x/sys");
        sys.IsDirect.Should().BeFalse();
    }
}
