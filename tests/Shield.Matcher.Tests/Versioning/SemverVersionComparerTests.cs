using FluentAssertions;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class SemverVersionComparerTests
{
    private readonly SemverVersionComparer _comparer = new(Ecosystem.Npm);

    [Fact]
    public void LtExcludesBoundaryVersion()
    {
        VersionRange range = new(Lt: "1.2.3");

        _comparer.Satisfies("1.2.2", range).Should().BeTrue();
        _comparer.Satisfies("1.2.3", range).Should().BeFalse();
    }

    [Fact]
    public void GtOrEqIncludesBoundaryVersion()
    {
        VersionRange range = new(GtOrEq: "1.0.0", Lt: "2.0.0");

        _comparer.Satisfies("1.0.0", range).Should().BeTrue();
        _comparer.Satisfies("1.99.99", range).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range).Should().BeFalse();
        _comparer.Satisfies("0.9.9", range).Should().BeFalse();
    }

    [Fact]
    public void PreReleaseIsLowerThanRelease()
    {
        // SemVer: 1.0.0-rc.1 < 1.0.0
        VersionRange range = new(GtOrEq: "1.0.0");

        _comparer.Satisfies("1.0.0-rc.1", range).Should().BeFalse();
        _comparer.Satisfies("1.0.0", range).Should().BeTrue();
    }

    [Fact]
    public void PreReleaseWithinPreReleaseRangeMatches()
    {
        VersionRange range = new(GtOrEq: "1.0.0-alpha", Lt: "1.0.0-rc.5");

        _comparer.Satisfies("1.0.0-beta", range).Should().BeTrue();
        _comparer.Satisfies("1.0.0-rc.5", range).Should().BeFalse();
    }

    [Fact]
    public void VPrefixInVersionIsTolerated()
    {
        VersionRange range = new(GtOrEq: "v1.0.0", Lt: "v2.0.0");

        _comparer.Satisfies("v1.5.0", range).Should().BeTrue();
        _comparer.Satisfies("1.5.0", range).Should().BeTrue();
    }

    [Fact]
    public void GarbageVersionDoesNotMatch()
    {
        VersionRange range = new(GtOrEq: "1.0.0");

        _comparer.Satisfies("not-a-version", range).Should().BeFalse();
    }
}
