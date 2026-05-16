using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class NugetVersionComparerTests
{
    private readonly NugetVersionComparer _comparer = new();

    [Fact]
    public void Bracket_inclusive_lower_paren_exclusive_upper_matches_correctly()
    {
        // [1.0,2.0) means >= 1.0 and < 2.0
        VersionRange? range = NugetVersionComparer.ParseNuGetRangeNotation("[1.0,2.0)");

        range.Should().NotBeNull();
        _comparer.Satisfies("1.0.0", range!).Should().BeTrue();
        _comparer.Satisfies("1.5.0", range!).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range!).Should().BeFalse();
        _comparer.Satisfies("0.9.0", range!).Should().BeFalse();
    }

    [Fact]
    public void Paren_exclusive_lower_bracket_inclusive_upper_matches_correctly()
    {
        // (1.0,2.0] means > 1.0 and <= 2.0
        VersionRange? range = NugetVersionComparer.ParseNuGetRangeNotation("(1.0,2.0]");

        range.Should().NotBeNull();
        _comparer.Satisfies("1.0.0", range!).Should().BeFalse();
        _comparer.Satisfies("1.0.1", range!).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range!).Should().BeTrue();
        _comparer.Satisfies("2.0.1", range!).Should().BeFalse();
    }

    [Fact]
    public void Open_lower_only_matches_anything_below_upper()
    {
        // (,2.0) means anything < 2.0
        VersionRange? range = NugetVersionComparer.ParseNuGetRangeNotation("(,2.0)");

        range.Should().NotBeNull();
        _comparer.Satisfies("0.0.1", range!).Should().BeTrue();
        _comparer.Satisfies("1.99.99", range!).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range!).Should().BeFalse();
    }

    [Fact]
    public void Direct_VersionRange_with_introduced_and_fixed_works()
    {
        VersionRange range = new(GtOrEq: "1.0.0", Lt: "2.0.0");

        _comparer.Satisfies("1.5.0", range).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range).Should().BeFalse();
    }
}
