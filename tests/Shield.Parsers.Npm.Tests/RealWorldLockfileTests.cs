using FluentAssertions;
using Shield.Core.Results;
using Xunit;

namespace Shield.Parsers.Npm.Tests;

// Regression tests pinned against real-world package-lock.json blobs we've scanned in
// production. Shield's snapshot for aaoa-dev/Overlay historically reported 439 items
// when the lockfile actually contains 113 — a 3.9x phantom over-count. Whatever drift
// produced that won't be caught by synthetic fixtures, so we snapshot the real bytes
// here and assert the exact count + a handful of representative names.
public sealed class RealWorldLockfileTests
{
    // aaoa-dev/Overlay@main package-lock.json — tailwindcss tree, npm lockfileVersion 3.
    // 114 entries in the `packages` map: 1 root ("") + 113 dependencies. Parser must
    // emit exactly 113 InventoryItems.
    [Fact]
    public async Task AaoaOverlayLockfileEmitsExactlyOneItemPerNonRootPackagesEntry()
    {
        NpmLockParser parser = new();
        await using Stream stream = FixtureLoader.Open("aaoa-overlay-real.json");

        ParseResult result = await parser.ParseAsync(
            stream,
            "package-lock.json",
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);

        // The fixture is verbatim from production. 113 = exactly the count of non-root
        // entries in its `packages` map. If this drifts up, the parser has regressed to
        // the phantom-emit pattern that produced 439 in earlier snapshots.
        result.Items.Should().HaveCount(113);

        // Direct dep is tailwindcss — assert it's flagged as such.
        result.Items.Should().ContainSingle(item => item.Name == "tailwindcss");
        result.Items.Single(item => item.Name == "tailwindcss").IsDirect.Should().BeTrue();

        // Sanity-check a representative transitive that the historical bogus snapshots
        // INCLUDED but the real lockfile doesn't — if it ever shows up here, the parser
        // is emitting phantom names.
        result
            .Items.Select(item => item.Name)
            .Should()
            .NotContain("@chevrotain/cst-dts-gen")
            .And.NotContain("mermaid")
            .And.NotContain("katex");
    }
}
