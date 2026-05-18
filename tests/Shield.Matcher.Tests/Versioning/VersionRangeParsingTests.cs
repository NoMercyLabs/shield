using FluentAssertions;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests.Versioning;

public class VersionRangeParsingTests
{
    [Fact]
    public void ParseOsvEventsIntroducedAndFixedYieldsHalfOpenRange()
    {
        const string events = """[ { "introduced": "1.0.0" }, { "fixed": "2.0.0" } ]""";

        IReadOnlyList<VersionRange> ranges = VersionRange.ParseOsvEvents(events);

        ranges.Should().HaveCount(1);
        ranges[0].GtOrEq.Should().Be("1.0.0");
        ranges[0].Lt.Should().Be("2.0.0");
    }

    [Fact]
    public void ParseOsvEventsIntroducedOnlyYieldsOpenLowerBound()
    {
        const string events = """[ { "introduced": "1.0.0" } ]""";

        IReadOnlyList<VersionRange> ranges = VersionRange.ParseOsvEvents(events);

        ranges.Should().HaveCount(1);
        ranges[0].GtOrEq.Should().Be("1.0.0");
        ranges[0].Lt.Should().BeNull();
    }

    [Fact]
    public void ParseOsvEventsIntroducedZeroTreatedAsUnboundedLower()
    {
        const string events = """[ { "introduced": "0" }, { "fixed": "1.5.0" } ]""";

        IReadOnlyList<VersionRange> ranges = VersionRange.ParseOsvEvents(events);

        ranges.Should().HaveCount(1);
        ranges[0].GtOrEq.Should().BeNull();
        ranges[0].Lt.Should().Be("1.5.0");
    }

    [Fact]
    public void ParseOsvEventsMultipleIntervalsYieldsMultipleRanges()
    {
        const string events = """
            [
              { "introduced": "1.0.0" }, { "fixed": "1.2.0" },
              { "introduced": "2.0.0" }, { "fixed": "2.3.0" }
            ]
            """;

        IReadOnlyList<VersionRange> ranges = VersionRange.ParseOsvEvents(events);

        ranges.Should().HaveCount(2);
        ranges[0].GtOrEq.Should().Be("1.0.0");
        ranges[0].Lt.Should().Be("1.2.0");
        ranges[1].GtOrEq.Should().Be("2.0.0");
        ranges[1].Lt.Should().Be("2.3.0");
    }

    [Fact]
    public void ParseOsvEventsLastAffectedYieldsInclusiveUpper()
    {
        const string events = """[ { "introduced": "1.0.0" }, { "last_affected": "1.4.2" } ]""";

        IReadOnlyList<VersionRange> ranges = VersionRange.ParseOsvEvents(events);

        ranges.Should().HaveCount(1);
        ranges[0].LtOrEq.Should().Be("1.4.2");
        ranges[0].Lt.Should().BeNull();
    }
}
