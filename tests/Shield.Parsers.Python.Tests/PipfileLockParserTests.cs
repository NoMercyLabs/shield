using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Python.Tests;

public class PipfileLockParserTests
{
    [Fact]
    public async Task ParseAsync_PipfileLock_StripsEqualsAndReturnsBothSections()
    {
        PythonDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Pipfile.lock"));

        ParseResult result = await parser.ParseAsync(
            stream,
            "Pipfile.lock",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Python);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "flask" && item.Version == "3.0.0");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "werkzeug" && item.Version == "3.0.1");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "pytest" && item.Version == "8.0.0");
    }
}
