using FluentAssertions;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class SemverVersionComparerTests
{
    private readonly SemverVersionComparer _comparer = new(Ecosystem.Npm);

    [Fact]
    public void Lt_excludes_boundary_version()
    {
        VersionRange range = new(Lt: "1.2.3");

        _comparer.Satisfies("1.2.2", range).Should().BeTrue();
        _comparer.Satisfies("1.2.3", range).Should().BeFalse();
    }

    [Fact]
    public void GtOrEq_includes_boundary_version()
    {
        VersionRange range = new(GtOrEq: "1.0.0", Lt: "2.0.0");

        _comparer.Satisfies("1.0.0", range).Should().BeTrue();
        _comparer.Satisfies("1.99.99", range).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range).Should().BeFalse();
        _comparer.Satisfies("0.9.9", range).Should().BeFalse();
    }

    [Fact]
    public void Pre_release_is_lower_than_release()
    {
        // SemVer: 1.0.0-rc.1 < 1.0.0
        VersionRange range = new(GtOrEq: "1.0.0");

        _comparer.Satisfies("1.0.0-rc.1", range).Should().BeFalse();
        _comparer.Satisfies("1.0.0", range).Should().BeTrue();
    }

    [Fact]
    public void Pre_release_within_pre_release_range_matches()
    {
        VersionRange range = new(GtOrEq: "1.0.0-alpha", Lt: "1.0.0-rc.5");

        _comparer.Satisfies("1.0.0-beta", range).Should().BeTrue();
        _comparer.Satisfies("1.0.0-rc.5", range).Should().BeFalse();
    }

    [Fact]
    public void V_prefix_in_version_is_tolerated()
    {
        VersionRange range = new(GtOrEq: "v1.0.0", Lt: "v2.0.0");

        _comparer.Satisfies("v1.5.0", range).Should().BeTrue();
        _comparer.Satisfies("1.5.0", range).Should().BeTrue();
    }

    [Fact]
    public void Garbage_version_does_not_match()
    {
        VersionRange range = new(GtOrEq: "1.0.0");

        _comparer.Satisfies("not-a-version", range).Should().BeFalse();
    }
}
