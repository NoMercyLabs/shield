using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class VcpkgVersionComparerTests
{
    private readonly VcpkgVersionComparer _comparer = new();

    [Fact]
    public void Port_version_breaks_ties_after_base_version()
    {
        // 1.2.3 < 1.2.3#1 < 1.2.3#5 — port-version counts after the base compares equal.
        VersionRange afterBase = new(GtOrEqExclusive: "1.2.3", Lt: "1.2.4");
        _comparer.Satisfies("1.2.3#1", afterBase).Should().BeTrue();
        _comparer.Satisfies("1.2.3#5", afterBase).Should().BeTrue();
        _comparer.Satisfies("1.2.3", afterBase).Should().BeFalse();
    }

    [Fact]
    public void Port_version_zero_is_equivalent_to_no_port_version()
    {
        VersionRange exact = new(Exact: new[] { "1.2.3" });
        _comparer.Satisfies("1.2.3#0", exact).Should().BeTrue();
        _comparer.Satisfies("1.2.3", exact).Should().BeTrue();
    }

    [Fact]
    public void Date_versions_compare_lexically()
    {
        // 2024-01-15 < 2024-03-20 < 2025-01-01 because yyyy-MM-dd is sortable as text.
        VersionRange range2024 = new(GtOrEq: "2024-01-01", Lt: "2025-01-01");
        _comparer.Satisfies("2024-01-15", range2024).Should().BeTrue();
        _comparer.Satisfies("2024-12-31", range2024).Should().BeTrue();
        _comparer.Satisfies("2025-01-01", range2024).Should().BeFalse();
    }

    [Fact]
    public void Date_versions_support_revision_suffix()
    {
        // 2024-01-15.0 < 2024-01-15.2 < 2024-01-16
        VersionRange afterFirstRev = new(GtOrEqExclusive: "2024-01-15.0", Lt: "2024-01-16");
        _comparer.Satisfies("2024-01-15.2", afterFirstRev).Should().BeTrue();
        _comparer.Satisfies("2024-01-15.0", afterFirstRev).Should().BeFalse();
    }

    [Fact]
    public void Semver_pre_release_sorts_below_release()
    {
        // 1.0.0-alpha < 1.0.0 in the version-semver scheme.
        VersionRange before = new(GtOrEq: "0.9.0", Lt: "1.0.0");
        _comparer.Satisfies("1.0.0-alpha", before).Should().BeTrue();
        _comparer.Satisfies("1.0.0", before).Should().BeFalse();
    }

    [Fact]
    public void Relaxed_version_compares_segment_wise_numerically()
    {
        // Versions with no SemVer prerelease and arbitrary segment counts: 1.2 < 1.2.0.1
        VersionRange exact = new(Exact: new[] { "1.2" });
        _comparer.Satisfies("1.2.0", exact).Should().BeTrue();

        VersionRange afterOnePointTwo = new(GtOrEqExclusive: "1.2", Lt: "1.3");
        _comparer.Satisfies("1.2.0.1", afterOnePointTwo).Should().BeTrue();
    }

    [Fact]
    public void Port_version_with_date_version()
    {
        // 2024-01-15#3 > 2024-01-15
        VersionRange afterDate = new(GtOrEqExclusive: "2024-01-15", Lt: "2024-01-16");
        _comparer.Satisfies("2024-01-15#3", afterDate).Should().BeTrue();
        _comparer.Satisfies("2024-01-15", afterDate).Should().BeFalse();
    }
}
