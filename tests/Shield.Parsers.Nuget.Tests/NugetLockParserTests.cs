using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Nuget.Tests;

public class NugetLockParserTests
{
    [Fact]
    public async Task ParseAsync_PackagesLockJson_ReturnsDirectAndTransitive()
    {
        NugetLockParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "packages.lock.json")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "packages.lock.json",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Diagnostics.Should().BeEmpty();
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Nuget);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Newtonsoft.Json" && item.Version == "13.0.3" && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Serilog.Sinks.File" && item.Version == "6.0.0" && !item.IsDirect
            );
    }

    [Fact]
    public async Task ParseAsync_CsprojFallback_FlagsLockfileMissingAndReturnsDirects()
    {
        NugetLockParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "Sample.csproj.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "Sample.csproj",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Diagnostics.Should().ContainKey("lockfileMissing");
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Nuget && item.IsDirect);
    }
}
