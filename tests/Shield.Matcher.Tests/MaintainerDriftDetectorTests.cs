using FluentAssertions;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Matcher.Tests;

public class MaintainerDriftDetectorTests
{
    private readonly MaintainerDriftDetector _detector = new();

    private static PackageMeta Meta(
        string maintainersJson,
        DateTime? publishedAt = null,
        bool deprecated = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "lodash",
            Version = "1.0.0",
            PublishedAt = publishedAt,
            MaintainersJson = maintainersJson,
            Deprecated = deprecated,
            FetchedAt = DateTime.UtcNow,
        };

    [Fact]
    public void NewMaintainerPlusRecentPublishEmitsHighSeverityDrift()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""["alice"]""");
        PackageMeta current = Meta("""["alice","mallory"]""", publishedAt: now.AddHours(-1));

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().HaveCount(1);
        drifts[0].Severity.Should().Be(Severity.High);
        drifts[0].Feed.Should().Be(Feed.NpmRegistry);
        drifts[0].ExternalId.Should().StartWith("drift:lodash:new-maintainer-immediate-publish:");
    }

    [Fact]
    public void NewMaintainerWithNoRecentPublishEmitsNothing()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""["alice"]""");
        PackageMeta current = Meta("""["alice","mallory"]""", publishedAt: now.AddDays(-30));

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().BeEmpty();
    }

    [Fact]
    public void DroppedMaintainerEmitsMediumSeverityDrift()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""["alice","bob"]""");
        PackageMeta current = Meta("""["alice"]""");

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().ContainSingle(advisory => advisory.Severity == Severity.Medium);
        drifts.Single().ExternalId.Should().Contain(":maintainer-dropped:");
    }

    [Fact]
    public void DeprecationFlipEmitsLowSeverityDrift()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""["alice"]""", deprecated: false);
        PackageMeta current = Meta("""["alice"]""", deprecated: true);

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().ContainSingle(advisory => advisory.Severity == Severity.Low);
        drifts.Single().ExternalId.Should().Contain(":deprecated:");
    }

    [Fact]
    public void NoChangeEmitsNothing()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""["alice","bob"]""");
        PackageMeta current = Meta("""["alice","bob"]""");

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().BeEmpty();
    }

    [Fact]
    public void NullPreviousEmitsNothingFirstObservation()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta current = Meta("""["alice","bob"]""", publishedAt: now);

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous: null,
            current,
            now
        );

        drifts.Should().BeEmpty();
    }

    [Fact]
    public void ObjectFormMaintainersWithNamePropertyParsedCorrectly()
    {
        DateTime now = DateTime.UtcNow;
        PackageMeta previous = Meta("""[{"name":"alice"}]""");
        PackageMeta current = Meta(
            """[{"name":"alice"},{"name":"mallory"}]""",
            publishedAt: now.AddMinutes(-30)
        );

        IReadOnlyList<Advisory> drifts = _detector.Detect(
            Ecosystem.Npm,
            "lodash",
            previous,
            current,
            now
        );

        drifts.Should().ContainSingle(advisory => advisory.Severity == Severity.High);
    }
}
