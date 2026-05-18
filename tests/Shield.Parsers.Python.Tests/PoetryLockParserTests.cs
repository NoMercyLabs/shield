using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Python.Tests;

public class PoetryLockParserTests
{
    [Fact]
    public async Task ParseAsync_PoetryLock_ReturnsPackages()
    {
        PythonDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "poetry.lock"));

        ParseResult result = await parser.ParseAsync(stream, "poetry.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Python);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "requests" && item.Version == "2.31.0");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "charset-normalizer" && item.Version == "3.3.2");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "pytest" && item.Version == "8.0.0");

        InventoryItem requests = result.Items.Single(item => item.Name == "requests");
        requests.ParentChain.Should().Contain("charset-normalizer").And.Contain("idna");
    }
}
