using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Nuget.Tests;

public class NugetDependencyParserTests
{
    [Fact]
    public async Task ParseAsync_PackagesLockJson_ReturnsDirectAndTransitive()
    {
        NugetDependencyParser parser = new();
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
        NugetDependencyParser parser = new();
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
        result
            .Items.Should()
            .OnlyContain(item => item.Ecosystem == Ecosystem.Nuget && item.IsDirect);
    }

    [Fact]
    public async Task ParseAsync_DirectoryPackagesProps_EmitsThreeItemsAllDirect()
    {
        NugetDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "Directory.Packages.props.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "Directory.Packages.props",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
        result
            .Items.Should()
            .OnlyContain(item => item.Ecosystem == Ecosystem.Nuget && item.IsDirect);
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "Newtonsoft.Json" && item.Version == "13.0.3");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "Serilog" && item.Version == "4.0.0");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "FluentAssertions" && item.Version == "7.0.0");
    }

    [Fact]
    public async Task ParseAsync_CpmCsproj_NoVersionAttribute_EmitsNothing()
    {
        NugetDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "CpmCsproj.csproj.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "CpmCsproj.csproj",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().BeEmpty();
        result.Diagnostics.Should().ContainKey("lockfileMissing");
    }

    [Fact]
    public async Task ParseAsync_LegacyCsproj_WithVersionAttribute_EmitsAsToday()
    {
        NugetDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "Sample.csproj.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "Sample.csproj",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(2);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Newtonsoft.Json" && item.Version == "13.0.3" && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Serilog" && item.Version == "4.0.0" && item.IsDirect
            );
    }

    [Fact]
    public async Task ParseAsync_GlobalPackageReference_IsEmitted()
    {
        NugetDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "GlobalPackageReference.props.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "Directory.Packages.props",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(2);
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Microsoft.SourceLink.GitHub"
                && item.Version == "8.0.0"
                && item.IsDirect
            );
        result
            .Items.Should()
            .ContainSingle(item =>
                item.Name == "Newtonsoft.Json" && item.Version == "13.0.3" && item.IsDirect
            );
    }

    [Fact]
    public async Task ParseAsync_EnvVariantPropsFilename_IsRecognisedAsCpm()
    {
        NugetDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(
            Path.Combine("Fixtures", "Directory.Packages.props.txt")
        );

        ParseResult result = await parser.ParseAsync(
            stream,
            "Directory.Packages.Production.props",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(3);
    }
}
