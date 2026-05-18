using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class GradleVersionComparerTests
{
    private readonly GradleVersionComparer _comparer = new();

    [Fact]
    public void Two_segment_maven_versions_normalize_to_three_segments()
    {
        VersionRange range = new(GtOrEq: "1.0", Lt: "2.0");

        _comparer.Satisfies("1.5", range).Should().BeTrue();
        _comparer.Satisfies("2.0", range).Should().BeFalse();
    }

    [Fact]
    public void Snapshot_qualifier_is_treated_as_pre_release()
    {
        // 1.0-SNAPSHOT < 1.0 under Maven qualifier ordering, so it's outside [1.0, 2.0).
        VersionRange range = new(GtOrEq: "1.0", Lt: "2.0");
        _comparer.Satisfies("1.0-SNAPSHOT", range).Should().BeFalse();

        // But it IS within [0.9, 2.0): pre-release of 1.0 is greater than any 0.9.x.
        VersionRange wider = new(GtOrEq: "0.9", Lt: "2.0");
        _comparer.Satisfies("1.0-SNAPSHOT", wider).Should().BeTrue();
    }

    [Fact]
    public void Maven_qualifier_order_alpha_beta_milestone_rc_release_sp()
    {
        // 1.0-alpha < 1.0-beta < 1.0-milestone < 1.0-rc < 1.0 (== 1.0-ga == 1.0-final) < 1.0-sp1
        VersionRange beforeRelease = new(GtOrEq: "1.0-alpha", Lt: "1.0");
        _comparer.Satisfies("1.0-alpha", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0-beta", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0-milestone", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0-rc", beforeRelease).Should().BeTrue();
        _comparer.Satisfies("1.0", beforeRelease).Should().BeFalse();
    }

    [Fact]
    public void Final_ga_release_are_equivalent_to_no_qualifier()
    {
        VersionRange exact = new(Exact: new[] { "1.0" });
        _comparer.Satisfies("1.0", exact).Should().BeTrue();
        _comparer.Satisfies("1.0-ga", exact).Should().BeTrue();
        _comparer.Satisfies("1.0-final", exact).Should().BeTrue();
        _comparer.Satisfies("1.0-release", exact).Should().BeTrue();
    }

    [Fact]
    public void Sp_qualifier_sorts_after_release()
    {
        // 1.0-sp1 > 1.0 — SP (Service Pack) is the only qualifier above the release rank.
        VersionRange afterRelease = new(GtOrEqExclusive: "1.0", Lt: "1.1");
        _comparer.Satisfies("1.0-sp1", afterRelease).Should().BeTrue();
        _comparer.Satisfies("1.0-rc1", afterRelease).Should().BeFalse();
        _comparer.Satisfies("1.0", afterRelease).Should().BeFalse();
    }

    [Fact]
    public void Trailing_zero_segments_are_equivalent()
    {
        VersionRange exact = new(Exact: new[] { "1.0" });
        _comparer.Satisfies("1.0.0", exact).Should().BeTrue();
        _comparer.Satisfies("1.0.0.0", exact).Should().BeTrue();
    }

    [Fact]
    public void Dot_and_dash_qualifier_separators_are_equivalent()
    {
        VersionRange exact = new(Exact: new[] { "1.0-alpha-1" });
        _comparer.Satisfies("1.0.alpha.1", exact).Should().BeTrue();
        _comparer.Satisfies("1.0-alpha.1", exact).Should().BeTrue();
    }
}
