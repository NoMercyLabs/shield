using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class GoModVersionComparerTests
{
    private readonly GoModVersionComparer _comparer = new();

    [Fact]
    public void VPrefixIsOptionalForMatching()
    {
        VersionRange exact = new(Exact: new[] { "v1.2.3" });
        _comparer.Satisfies("1.2.3", exact).Should().BeTrue();
        _comparer.Satisfies("v1.2.3", exact).Should().BeTrue();
    }

    [Fact]
    public void PseudoVersionOrdersBetweenBaseAndNextPatch()
    {
        // v0.0.0-20191023135150-abc1234def56 is treated as a pre-release of 0.0.0; sorts
        // below v0.0.1 and below v0.0.0 release.
        VersionRange beforeFirstRelease = new(GtOrEq: "0.0.0-0", Lt: "0.0.0");
        _comparer
            .Satisfies("v0.0.0-20191023135150-abc1234def56", beforeFirstRelease)
            .Should()
            .BeTrue();
        _comparer.Satisfies("v0.0.0", beforeFirstRelease).Should().BeFalse();
    }

    [Fact]
    public void PseudoVersionsOrderByTimestampSegment()
    {
        // Two pseudo-versions on the same base: the one with the later timestamp wins.
        VersionRange afterEarlier = new(
            GtOrEqExclusive: "v0.0.0-20191023135150-abc1234def56",
            Lt: "v1.0.0"
        );
        _comparer.Satisfies("v0.0.0-20191024999999-zzz9999zzz99", afterEarlier).Should().BeTrue();
        _comparer.Satisfies("v0.0.0-20190101000000-aaa1111aaa11", afterEarlier).Should().BeFalse();
    }

    [Fact]
    public void IncompatibleSuffixSortsAboveSameBase()
    {
        // v2.0.0+incompatible > v2.0.0 — the suffix flags a non-/v2-path module and counts
        // for ordering, unlike normal SemVer build metadata.
        VersionRange afterPlain = new(GtOrEqExclusive: "v2.0.0", Lt: "v3.0.0");
        _comparer.Satisfies("v2.0.0+incompatible", afterPlain).Should().BeTrue();
        _comparer.Satisfies("v2.0.0", afterPlain).Should().BeFalse();
    }

    [Fact]
    public void PreReleaseSortsBelowRelease()
    {
        // SemVer 2 pre-release ordering: v1.0.0-alpha < v1.0.0-beta < v1.0.0
        VersionRange preReleaseRange = new(GtOrEq: "1.0.0-alpha", Lt: "1.0.0");
        _comparer.Satisfies("v1.0.0-alpha", preReleaseRange).Should().BeTrue();
        _comparer.Satisfies("v1.0.0-beta", preReleaseRange).Should().BeTrue();
        _comparer.Satisfies("v1.0.0", preReleaseRange).Should().BeFalse();
    }

    [Fact]
    public void NumericPreReleaseIdentifiersSortBelowAlphanumeric()
    {
        // Per SemVer 2 §11.4.3, numeric identifier "1" < alphanumeric "alpha".
        VersionRange exact = new(Exact: new[] { "1.0.0-1" });
        _comparer.Satisfies("v1.0.0-1", exact).Should().BeTrue();

        VersionRange before = new(GtOrEq: "1.0.0-1", Lt: "1.0.0-alpha");
        _comparer.Satisfies("v1.0.0-1", before).Should().BeTrue();
        _comparer.Satisfies("v1.0.0-alpha", before).Should().BeFalse();
    }

    [Fact]
    public void BareMajorIsVDotZeroDotZero()
    {
        VersionRange exact = new(Exact: new[] { "v1.0.0" });
        _comparer.Satisfies("v1", exact).Should().BeTrue();
        _comparer.Satisfies("v1.0", exact).Should().BeTrue();
    }
}
