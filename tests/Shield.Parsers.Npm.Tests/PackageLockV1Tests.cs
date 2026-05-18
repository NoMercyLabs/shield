using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Npm;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class PackageLockV1Tests
{
    [Fact]
    public async Task Parses_v1_lockfile_with_nested_deps()
    {
        NpmLockParser parser = new();
        using Stream stream = FixtureLoader.Open("package-lock.v1.json");

        ParseResult result = await parser.ParseAsync(
            stream,
            "package-lock.json",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(5);

        InventoryItem express = result.Items.Single(i => i.Name == "express");
        express.Version.Should().Be("4.18.2");
        express.IsDirect.Should().BeTrue();

        InventoryItem lodash = result.Items.Single(i => i.Name == "lodash");
        lodash.IsDirect.Should().BeTrue();

        // 'negotiator' is nested under express's dependencies; transitive with parent chain
        InventoryItem negotiator = result.Items.Single(i => i.Name == "negotiator");
        negotiator.IsDirect.Should().BeFalse();
        negotiator.ParentChain.Should().Contain("express").And.NotBe("[]");
    }
}
