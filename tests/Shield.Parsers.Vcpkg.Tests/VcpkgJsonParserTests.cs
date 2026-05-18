using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Vcpkg.Tests;

public class VcpkgJsonParserTests
{
    [Fact]
    public async Task ParseAsync_VcpkgJson_ReturnsDependenciesWithOverrideAndWildcardFallback()
    {
        VcpkgJsonParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "vcpkg.json"));

        ParseResult result = await parser.ParseAsync(stream, "vcpkg.json", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Vcpkg);
        // fmt picks up the override version.
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "fmt" && item.Version == "10.2.1");
        // zlib has no version anywhere -> wildcard.
        result.Items.Should().ContainSingle(item => item.Name == "zlib" && item.Version == "*");
        // boost-asio gets its inline version>= constraint.
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "boost-asio" && item.Version == "1.84.0");
        result.Diagnostics.Should().ContainKey("baseline");
    }
}
