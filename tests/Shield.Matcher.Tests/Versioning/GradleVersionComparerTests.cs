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
        // 1.0-SNAPSHOT < 1.0 under semver pre-release ordering, so it's outside [1.0, 2.0).
        VersionRange range = new(GtOrEq: "1.0", Lt: "2.0");
        _comparer.Satisfies("1.0-SNAPSHOT", range).Should().BeFalse();

        // But it IS within [0.9, 2.0): pre-release of 1.0 is greater than any 0.9.x.
        VersionRange wider = new(GtOrEq: "0.9", Lt: "2.0");
        _comparer.Satisfies("1.0-SNAPSHOT", wider).Should().BeTrue();
    }
}
