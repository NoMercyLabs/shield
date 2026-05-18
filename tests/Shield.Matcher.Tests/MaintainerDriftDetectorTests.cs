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
    public void New_maintainer_plus_recent_publish_emits_high_severity_drift()
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
    public void New_maintainer_with_no_recent_publish_emits_nothing()
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
    public void Dropped_maintainer_emits_medium_severity_drift()
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
    public void Deprecation_flip_emits_low_severity_drift()
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
    public void No_change_emits_nothing()
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
    public void Null_previous_emits_nothing_first_observation()
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
    public void Object_form_maintainers_with_name_property_parsed_correctly()
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
