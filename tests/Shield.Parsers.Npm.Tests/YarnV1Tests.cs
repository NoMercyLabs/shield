using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class YarnV1Tests
{
    [Fact]
    public async Task ParsesYarnV1Lockfile()
    {
        NpmLockParser parser = new();
        using Stream stream = FixtureLoader.Open("yarn.v1.lock");

        ParseResult result = await parser.ParseAsync(stream, "yarn.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(7);
        result.Diagnostics.Should().ContainKey("format");
        result.Diagnostics["format"].Should().Contain("v1");

        InventoryItem vue = result.Items.Single(i => i.Name == "vue");
        vue.Version.Should().Be("3.4.15");
        vue.Ecosystem.Should().Be(Ecosystem.Npm);

        InventoryItem shared = result.Items.Single(i => i.Name == "@vue/shared");
        shared.Version.Should().Be("3.4.15");
    }
}
