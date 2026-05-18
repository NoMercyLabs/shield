using FluentAssertions;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

// Cargo uses SemverVersionComparer because Cargo's version COMPARISON is SemVer 2.0 exactly.
// These tests document the Cargo-shaped inputs we expect to flow through the matcher so a
// future refactor can't silently break them.
public class CargoVersionComparerTests
{
    private readonly SemverVersionComparer _comparer = new(Ecosystem.Rust);

    [Fact]
    public void Cargo_versions_compare_under_semver_rules()
    {
        // Standard ordering: 1.0.0 < 1.0.1 < 1.1.0 < 2.0.0
        VersionRange exact = new(Exact: new[] { "1.2.3" });
        _comparer.Satisfies("1.2.3", exact).Should().BeTrue();
        _comparer.Satisfies("1.2.4", exact).Should().BeFalse();
    }

    [Fact]
    public void Pre_release_sorts_below_release()
    {
        VersionRange beforeRelease = new(GtOrEq: "0.9.0", Lt: "1.0.0");
        _comparer.Satisfies("1.0.0-alpha", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0.0-beta.2", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0.0", beforeRelease).Should().BeFalse();
    }

    [Fact]
    public void Build_metadata_is_ignored_for_ordering()
    {
        // SemVer 2 §10: build metadata MUST be ignored when determining version precedence.
        // 1.0.0+abc == 1.0.0+def == 1.0.0
        VersionRange exact = new(Exact: new[] { "1.0.0" });
        _comparer.Satisfies("1.0.0+commit.abc1234", exact).Should().BeTrue();
        _comparer.Satisfies("1.0.0+sha256.def5678", exact).Should().BeTrue();
    }

    [Fact]
    public void Numeric_pre_release_identifiers_sort_below_alphanumeric()
    {
        // SemVer 2 §11.4.3 — numeric ID "1" < alphanumeric "alpha".
        VersionRange between = new(GtOrEq: "1.0.0-1", Lt: "1.0.0-alpha");
        _comparer.Satisfies("1.0.0-1", between).Should().BeTrue();
        _comparer.Satisfies("1.0.0-2", between).Should().BeTrue();
        _comparer.Satisfies("1.0.0-alpha", between).Should().BeFalse();
    }

    [Fact]
    public void Multi_segment_pre_release_compares_segment_wise()
    {
        // 1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta
        VersionRange exact = new(Exact: new[] { "1.0.0-alpha.beta" });
        _comparer.Satisfies("1.0.0-alpha.beta", exact).Should().BeTrue();

        VersionRange afterAlpha1 = new(GtOrEqExclusive: "1.0.0-alpha.1", Lt: "1.0.0");
        _comparer.Satisfies("1.0.0-alpha.2", afterAlpha1).Should().BeTrue();
        _comparer.Satisfies("1.0.0-beta", afterAlpha1).Should().BeTrue();
    }
}
