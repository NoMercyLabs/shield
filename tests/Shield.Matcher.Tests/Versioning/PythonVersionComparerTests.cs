using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class PythonVersionComparerTests
{
    private readonly PythonVersionComparer _comparer = new();

    [Fact]
    public void PreReleaseSortsBelowRelease()
    {
        // 1.0a1, 1.0b1, 1.0rc1 < 1.0 — the classic PEP 440 ordering.
        VersionRange beforeRelease = new(GtOrEq: "0.9", Lt: "1.0");
        _comparer.Satisfies("1.0a1", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0b2", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0rc1", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0", beforeRelease).Should().BeFalse();
    }

    [Fact]
    public void PostReleaseSortsAboveRelease()
    {
        // 1.0.post1 > 1.0 — unique to PEP 440 (SemVer has no equivalent).
        VersionRange afterRelease = new(GtOrEqExclusive: "1.0", Lt: "2.0");
        _comparer.Satisfies("1.0.post1", afterRelease).Should().BeTrue();
        _comparer.Satisfies("1.0.post0", afterRelease).Should().BeTrue();
        _comparer.Satisfies("1.0", afterRelease).Should().BeFalse();
    }

    [Fact]
    public void DevReleaseSortsBelowEverything()
    {
        // 1.0.dev0 < 1.0a0 < 1.0
        VersionRange beforeAlpha = new(GtOrEq: "0", Lt: "1.0a0");
        _comparer.Satisfies("1.0.dev0", beforeAlpha).Should().BeTrue();
        _comparer.Satisfies("1.0a0", beforeAlpha).Should().BeFalse();
    }

    [Fact]
    public void PreReleaseLabelAliasesNormalise()
    {
        // a == alpha, b == beta, c == rc == pre == preview
        VersionRange exact = new(Exact: new[] { "1.0a1" });
        _comparer.Satisfies("1.0alpha1", exact).Should().BeTrue();

        VersionRange exactBeta = new(Exact: new[] { "1.0b2" });
        _comparer.Satisfies("1.0beta2", exactBeta).Should().BeTrue();

        VersionRange exactRc = new(Exact: new[] { "1.0rc1" });
        _comparer.Satisfies("1.0c1", exactRc).Should().BeTrue();
        _comparer.Satisfies("1.0pre1", exactRc).Should().BeTrue();
        _comparer.Satisfies("1.0preview1", exactRc).Should().BeTrue();
    }

    [Fact]
    public void EpochsDominateReleaseNumbering()
    {
        // 1!1.0 > 9999.0 — epoch trumps everything.
        VersionRange afterEverything = new(GtOrEq: "1!0", Lt: "2!0");
        _comparer.Satisfies("1!1.0", afterEverything).Should().BeTrue();
        _comparer.Satisfies("9999.99", afterEverything).Should().BeFalse();
    }

    [Fact]
    public void TrailingZeroSegmentsAreIgnored()
    {
        // 1.0 == 1.0.0 == 1.0.0.0
        VersionRange exact = new(Exact: new[] { "1.0" });
        _comparer.Satisfies("1.0.0", exact).Should().BeTrue();
        _comparer.Satisfies("1.0.0.0", exact).Should().BeTrue();
    }

    [Fact]
    public void SeparatorVariantsInPreReleaseAreEquivalent()
    {
        // PEP 440 permits "1.0a1", "1.0.a1", "1.0-a1", "1.0_a1" — all canonicalise.
        VersionRange exact = new(Exact: new[] { "1.0a1" });
        _comparer.Satisfies("1.0.a1", exact).Should().BeTrue();
        _comparer.Satisfies("1.0-a1", exact).Should().BeTrue();
        _comparer.Satisfies("1.0_a1", exact).Should().BeTrue();
    }
}
