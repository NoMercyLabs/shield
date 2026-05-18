using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class GemVersionComparerTests
{
    private readonly GemVersionComparer _comparer = new();

    [Fact]
    public void PreReleaseStringSegmentsSortBelowRelease()
    {
        // 1.0.alpha < 1.0 — string segment makes it a pre-release.
        VersionRange beforeRelease = new(GtOrEq: "0.9", Lt: "1.0");
        _comparer.Satisfies("1.0.alpha", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0.beta.2", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0.rc.1", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0", beforeRelease).Should().BeFalse();
    }

    [Fact]
    public void StringSegmentsSortLexically()
    {
        // 1.0.a < 1.0.b < 1.0.c — string segments compare via ordinal compare.
        VersionRange aOnly = new(GtOrEq: "1.0.a", Lt: "1.0.b");
        _comparer.Satisfies("1.0.a", aOnly).Should().BeTrue();
        _comparer.Satisfies("1.0.b", aOnly).Should().BeFalse();
    }

    [Fact]
    public void TrailingZeroSegmentsAreEquivalent()
    {
        // 1.0 == 1.0.0 == 1.0.0.0
        VersionRange exact = new(Exact: new[] { "1.0" });
        _comparer.Satisfies("1.0.0", exact).Should().BeTrue();
        _comparer.Satisfies("1.0.0.0", exact).Should().BeTrue();
        _comparer.Satisfies("1", exact).Should().BeTrue();
    }

    [Fact]
    public void PreReleaseWithNumberOrdersCorrectly()
    {
        // 1.0.alpha.1 < 1.0.alpha.2 < 1.0.beta.1 < 1.0
        VersionRange alphaRange = new(GtOrEq: "1.0.alpha.1", Lt: "1.0.beta");
        _comparer.Satisfies("1.0.alpha.1", alphaRange).Should().BeTrue();
        _comparer.Satisfies("1.0.alpha.5", alphaRange).Should().BeTrue();
        _comparer.Satisfies("1.0.beta", alphaRange).Should().BeFalse();
    }

    [Fact]
    public void IntegerReleaseAboveStringPreRelease()
    {
        // 1.0 > 1.0.alpha — integer segment wins against string segment.
        VersionRange afterPre = new(GtOrEqExclusive: "1.0.alpha", Lt: "1.1");
        _comparer.Satisfies("1.0", afterPre).Should().BeTrue();
        _comparer.Satisfies("1.0.alpha", afterPre).Should().BeFalse();
    }

    [Fact]
    public void LetterDigitTransitionCreatesTokenBoundary()
    {
        // 1.0.a1 tokenises as [1, 0, "a", 1] not [1, 0, "a1"], so 1.0.a1 == 1.0.a.1.
        VersionRange exact = new(Exact: new[] { "1.0.a1" });
        _comparer.Satisfies("1.0.a.1", exact).Should().BeTrue();
    }
}
