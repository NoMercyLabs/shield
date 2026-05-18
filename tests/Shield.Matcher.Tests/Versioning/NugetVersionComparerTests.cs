using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class NugetVersionComparerTests
{
    private readonly NugetVersionComparer _comparer = new();

    [Fact]
    public void BracketInclusiveLowerParenExclusiveUpperMatchesCorrectly()
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
    public void ParenExclusiveLowerBracketInclusiveUpperMatchesCorrectly()
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
    public void OpenLowerOnlyMatchesAnythingBelowUpper()
    {
        // (,2.0) means anything < 2.0
        VersionRange? range = NugetVersionComparer.ParseNuGetRangeNotation("(,2.0)");

        range.Should().NotBeNull();
        _comparer.Satisfies("0.0.1", range!).Should().BeTrue();
        _comparer.Satisfies("1.99.99", range!).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range!).Should().BeFalse();
    }

    [Fact]
    public void DirectVersionRangeWithIntroducedAndFixedWorks()
    {
        VersionRange range = new(GtOrEq: "1.0.0", Lt: "2.0.0");

        _comparer.Satisfies("1.5.0", range).Should().BeTrue();
        _comparer.Satisfies("2.0.0", range).Should().BeFalse();
    }
}
