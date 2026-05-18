using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Python.Tests;

public class RequirementsTxtParserTests
{
    [Fact]
    public async Task ParseAsync_RequirementsTxt_PinnedAndUnpinned()
    {
        PythonDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "requirements.txt"));

        ParseResult result = await parser.ParseAsync(stream, "requirements.txt", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(5);

        result.Items.Should().ContainSingle(item => item.Name == "django" && item.Version == "5.0.1");
        result.Items.Should().ContainSingle(item => item.Name == "requests" && item.Version == "2.31.0");
        result.Items.Should().ContainSingle(item => item.Name == "flask" && item.Version == string.Empty);
        result.Items.Should().ContainSingle(item => item.Name == "pytest" && item.Version == string.Empty);
        result.Items.Should().ContainSingle(item => item.Name == "black" && item.Version == string.Empty);

        result.Diagnostics.Should().ContainKey("unpinned");
    }
}
