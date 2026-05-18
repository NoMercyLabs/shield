using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Rust.Tests;

public class CargoLockParserTests
{
    [Fact]
    public async Task ParseAsyncCargoLockSkipsLocalPackageAndReturnsRegistryDeps()
    {
        RustDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Cargo.lock"));

        ParseResult result = await parser.ParseAsync(stream, "Cargo.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        // demo-app has no source -> skipped. serde, serde_derive, tokio remain.
        result.Items.Should().HaveCount(3);
        result.Items.Should().OnlyContain(item => item.Ecosystem == Ecosystem.Rust);
        result.Items.Should().NotContain(item => item.Name == "demo-app");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "serde" && item.Version == "1.0.195");
        result
            .Items.Should()
            .ContainSingle(item => item.Name == "tokio" && item.Version == "1.35.1");

        InventoryItem serde = result.Items.Single(item => item.Name == "serde");
        serde.ParentChain.Should().Contain("serde_derive");
    }

    [Fact]
    public async Task ParseAsyncCargoLockAllRegistryItemsDefaultIsDirect()
    {
        RustDependencyParser parser = new();
        await using FileStream stream = File.OpenRead(Path.Combine("Fixtures", "Cargo.lock"));

        ParseResult result = await parser.ParseAsync(stream, "Cargo.lock", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        // Cargo.lock can't distinguish direct from transitive without Cargo.toml.
        // The parser defaults all registry entries to IsDirect=true per migration convention.
        result.Items.Should().OnlyContain(item => item.IsDirect);
    }
}
