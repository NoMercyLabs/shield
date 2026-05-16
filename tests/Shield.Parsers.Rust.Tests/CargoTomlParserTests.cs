using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Rust.Tests;

public class CargoTomlParserTests
{
    [Fact]
    public async Task ParseAsync_CargoToml_ReturnsDirectDependenciesWithoutVersions()
    {
        RustLockParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Cargo.toml"));

        ParseResult result = await parser.ParseAsync(stream, "Cargo.toml", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Rust && item.IsDirect);
        result.Items.Select(item => item.Name).Should().BeEquivalentTo(new[] { "serde", "tokio", "mockito" });
        result.Items.Should().OnlyContain(item => item.Version == string.Empty);
    }
}
