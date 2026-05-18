using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Npm;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class PnpmTests
{
    [Fact]
    public async Task Parses_pnpm_lockfile_with_direct_flag_from_importers()
    {
        NpmLockParser parser = new();
        using Stream stream = FixtureLoader.Open("pnpm-lock.yaml");

        ParseResult result = await parser.ParseAsync(
            stream,
            "pnpm-lock.yaml",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(7);

        InventoryItem vue = result.Items.Single(i => i.Name == "vue");
        vue.Version.Should().Be("3.4.15");
        vue.IsDirect.Should().BeTrue();

        InventoryItem typescript = result.Items.Single(i => i.Name == "typescript");
        typescript.IsDirect.Should().BeTrue();

        InventoryItem shared = result.Items.Single(i => i.Name == "@vue/shared");
        shared.IsDirect.Should().BeFalse();
        shared.Version.Should().Be("3.4.15");
    }
}
