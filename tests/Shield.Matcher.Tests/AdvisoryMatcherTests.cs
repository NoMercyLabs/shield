using FluentAssertions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;
using Xunit;

namespace Shield.Matcher.Tests;

public class AdvisoryMatcherTests
{
    private static AdvisoryMatcher BuildMatcher()
        => new(new IVersionComparer[]
        {
            new SemverVersionComparer(Ecosystem.Npm),
            new SemverVersionComparer(Ecosystem.Composer),
            new NugetVersionComparer(),
            new GradleVersionComparer(),
        });

    private static InventorySnapshot Snapshot(int sourceId = 7, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            SourceId = sourceId,
            TakenAt = DateTime.UtcNow,
            ContentsSha = "sha",
            ItemCount = 0,
        };

    private static InventoryItem Item(int id, Ecosystem eco, string name, string version)
        => new()
        {
            Id = id,
            Ecosystem = eco,
            Name = name,
            Version = version,
            ParentChain = "[]",
            IsDirect = true,
        };

    private static Advisory Advisory(Ecosystem eco, string pkg, string externalId, string rangeJson,
        Severity severity = Severity.High)
        => new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.Osv,
            ExternalId = externalId,
            Ecosystem = eco,
            PackageName = pkg,
            AffectedRangesJson = rangeJson,
            Severity = severity,
            Summary = "summary",
            PublishedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            FetchedAt = DateTime.UtcNow,
        };

    [Fact]
    public void Match_emits_finding_when_version_in_affected_range()
    {
        AdvisoryMatcher matcher = BuildMatcher();
        InventorySnapshot snapshot = Snapshot();
        InventoryItem item = Item(1, Ecosystem.Npm, "lodash", "4.17.20");
        Advisory advisory = Advisory(
            Ecosystem.Npm,
            "lodash",
            "GHSA-test-1",
            """[ { "events": [ { "introduced": "0" }, { "fixed": "4.17.21" } ] } ]""");

        IReadOnlyList<Finding> findings = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            Array.Empty<Finding>(),
            DateTime.UtcNow);

        findings.Should().HaveCount(1);
        findings[0].State.Should().Be(FindingState.Open);
        findings[0].AdvisoryRefId.Should().Be(advisory.Id);
        findings[0].SourceId.Should().Be(snapshot.SourceId);
    }

    [Fact]
    public void Match_skips_when_version_outside_range()
    {
        AdvisoryMatcher matcher = BuildMatcher();
        InventorySnapshot snapshot = Snapshot();
        InventoryItem item = Item(1, Ecosystem.Npm, "lodash", "4.17.21");
        Advisory advisory = Advisory(
            Ecosystem.Npm,
            "lodash",
            "GHSA-test-2",
            """[ { "events": [ { "introduced": "0" }, { "fixed": "4.17.21" } ] } ]""");

        IReadOnlyList<Finding> findings = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            Array.Empty<Finding>(),
            DateTime.UtcNow);

        findings.Should().BeEmpty();
    }

    [Fact]
    public void Match_returns_existing_finding_with_updated_LastSeenAt_for_dedup()
    {
        AdvisoryMatcher matcher = BuildMatcher();
        InventorySnapshot snapshot = Snapshot();
        InventoryItem item = Item(1, Ecosystem.Npm, "lodash", "4.17.20");
        Advisory advisory = Advisory(
            Ecosystem.Npm,
            "lodash",
            "GHSA-dedup",
            """[ { "events": [ { "introduced": "0" }, { "fixed": "4.17.21" } ] } ]""");

        DateTime firstRun = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<Finding> first = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            Array.Empty<Finding>(),
            firstRun);

        Finding firstFinding = first.Single();
        Guid originalId = firstFinding.Id;

        DateTime secondRun = firstRun.AddDays(1);
        IReadOnlyList<Finding> second = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            new[] { firstFinding },
            secondRun);

        second.Should().HaveCount(1);
        second[0].Id.Should().Be(originalId);
        second[0].FirstSeenAt.Should().Be(firstRun);
        second[0].LastSeenAt.Should().Be(secondRun);
    }

    [Fact]
    public void Match_dedup_key_matches_DedupKey_compute()
    {
        AdvisoryMatcher matcher = BuildMatcher();
        InventorySnapshot snapshot = Snapshot(sourceId: 42);
        InventoryItem item = Item(1, Ecosystem.Nuget, "Newtonsoft.Json", "12.0.1");
        Advisory advisory = Advisory(
            Ecosystem.Nuget,
            "Newtonsoft.Json",
            "GHSA-nuget-1",
            """[ { "events": [ { "introduced": "0" }, { "fixed": "13.0.1" } ] } ]""");

        IReadOnlyList<Finding> findings = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            Array.Empty<Finding>(),
            DateTime.UtcNow);

        string expected = DedupKey.Compute(42, Ecosystem.Nuget, "Newtonsoft.Json", "GHSA-nuget-1");
        findings.Single().DedupKey.Should().Be(expected);
    }

    [Fact]
    public void Match_ignores_advisories_for_other_ecosystems()
    {
        AdvisoryMatcher matcher = BuildMatcher();
        InventorySnapshot snapshot = Snapshot();
        InventoryItem item = Item(1, Ecosystem.Npm, "shared-name", "1.0.0");
        Advisory advisory = Advisory(
            Ecosystem.Nuget,
            "shared-name",
            "GHSA-other-eco",
            """[ { "events": [ { "introduced": "0" } ] } ]""");

        IReadOnlyList<Finding> findings = matcher.Match(
            snapshot,
            new[] { item },
            new[] { advisory },
            Array.Empty<Finding>(),
            DateTime.UtcNow);

        findings.Should().BeEmpty();
    }
}
