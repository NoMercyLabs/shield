using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Parsers.Npm;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

public class PackageLockV3Tests
{
    [Fact]
    public async Task Parses_v3_lockfile_with_direct_and_transitive_deps()
    {
        NpmLockParser parser = new();
        using Stream stream = FixtureLoader.Open("package-lock.v3.json");

        ParseResult result = await parser.ParseAsync(
            stream,
            "package-lock.json",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Items.Should().HaveCount(8);

        InventoryItem vue = result.Items.Single(i => i.Name == "vue");
        vue.Version.Should().Be("3.4.15");
        vue.IsDirect.Should().BeTrue();
        vue.Ecosystem.Should().Be(Ecosystem.Npm);

        InventoryItem typescript = result.Items.Single(i => i.Name == "typescript");
        typescript.IsDirect.Should().BeTrue();

        InventoryItem shared = result.Items.Single(i => i.Name == "@vue/shared");
        shared.IsDirect.Should().BeFalse();
        shared.Version.Should().Be("3.4.15");

        // Nested entry (deduplicated copy under vue) carries the parent chain
        InventoryItem nested = result.Items.Single(i => i.Name == "nested-helper");
        nested.IsDirect.Should().BeFalse();
        nested.ParentChain.Should().Contain("vue").And.NotBe("[]");
    }
}
