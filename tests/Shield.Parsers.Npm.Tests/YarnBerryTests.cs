using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Npm;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class YarnBerryTests
{
    [Fact]
    public async Task Parses_yarn_berry_lockfile()
    {
        NpmLockParser parser = new();
        using Stream stream = FixtureLoader.Open("yarn.berry.lock");

        ParseResult result = await parser.ParseAsync(stream, "yarn.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(7);
        result.Diagnostics["format"].Should().Contain("berry");

        InventoryItem vue = result.Items.Single(i => i.Name == "vue");
        vue.Version.Should().Be("3.4.15");

        InventoryItem parser2 = result.Items.Single(i => i.Name == "@babel/parser");
        parser2.Version.Should().Be("7.23.6");
        parser2.Ecosystem.Should().Be(Ecosystem.Npm);
    }
}
