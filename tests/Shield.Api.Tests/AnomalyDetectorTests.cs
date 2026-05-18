using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.Findings;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// The new detector uses PackageMeta-driven popularity (rather than a hardcoded list) so
// every typosquat / scope-mismatch test seeds a popular reference package into FeedsDb
// before evaluating. Pure Evaluate() calls take an explicit popular-names set so the
// tests stay deterministic without needing a real PackageMetas table.
public sealed class AnomalyDetectorTests : IClassFixture<ShieldWebAppFactory>
{
    private static readonly IReadOnlySet<string> NoPopularNames = new HashSet<string>();
    private readonly ShieldWebAppFactory _factory;

    public AnomalyDetectorTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void EvaluateFlagsBrandNewPackage()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        DateTime now = DateTime.UtcNow;

        PackageMeta current = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "shiny-new-thing",
            Version = "0.1.0",
            PublishedAt = now.AddDays(-2),
            MaintainersJson = "[\"alice\", \"bob\"]",
            FetchedAt = now,
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "shiny-new-thing",
            "0.1.0",
            current,
            priorVersionMeta: null,
            now,
            NoPopularNames
        );

        flags.HasFlag(AnomalyFlags.BrandNew).Should().BeTrue();
    }

    [Fact]
    public void EvaluateDoesNotFlagBrandNewForOldPublishDate()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        DateTime now = DateTime.UtcNow;

        PackageMeta current = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "old-stable-thing",
            Version = "5.0.0",
            PublishedAt = now.AddDays(-365),
            MaintainersJson = "[\"alice\", \"bob\", \"carol\"]",
            FetchedAt = now,
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "old-stable-thing",
            "5.0.0",
            current,
            priorVersionMeta: null,
            now,
            NoPopularNames
        );

        flags.HasFlag(AnomalyFlags.BrandNew).Should().BeFalse();
        flags.HasFlag(AnomalyFlags.SingleMaintainer).Should().BeFalse();
    }

    [Fact]
    public void EvaluateFlagsTyposquatWhenCandidateIsLowTrafficNearPopularName()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        DateTime now = DateTime.UtcNow;

        IReadOnlySet<string> popular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lodash",
        };

        // "lodahs" — one transposition away from "lodash", low downloads, single maintainer.
        PackageMeta candidate = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "lodahs",
            Version = "1.0.0",
            PublishedAt = now.AddDays(-5),
            MaintainersJson = "[\"mallory\"]",
            WeeklyDownloads = 30,
            FetchedAt = now,
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "lodahs",
            "1.0.0",
            candidate,
            priorVersionMeta: null,
            now,
            popular
        );

        flags.HasFlag(AnomalyFlags.Typosquat).Should().BeTrue();
    }

    [Fact]
    public void EvaluateDoesNotFlagPopularPackageAsTyposquatEvenWhenNameSimilar()
    {
        // The y18n regression — `y18n` is Levenshtein 2 from `yarn` but has 100M+
        // downloads/week. New detector must not fire typosquat on a candidate whose own
        // download count clears the popularity threshold.
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        DateTime now = DateTime.UtcNow;

        IReadOnlySet<string> popular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "yarn",
        };

        PackageMeta candidate = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "y18n",
            Version = "4.0.3",
            PublishedAt = now.AddYears(-3),
            MaintainersJson = "[\"node-org\", \"bcoe\"]",
            WeeklyDownloads = 100_000_000,
            FetchedAt = now,
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "y18n",
            "4.0.3",
            candidate,
            priorVersionMeta: null,
            now,
            popular
        );

        flags.HasFlag(AnomalyFlags.Typosquat).Should().BeFalse();
    }

    [Fact]
    public void EvaluateDoesNotFlagTyposquatWithoutCandidateMetadata()
    {
        // Conservative fallback: without registry metadata we can't distinguish legit-but-
        // unknown from a real squat. Refuse to fire on name alone.
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        IReadOnlySet<string> popular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lodash",
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "lodahs",
            "1.0.0",
            current: null,
            priorVersionMeta: null,
            DateTime.UtcNow,
            popular
        );

        flags.HasFlag(AnomalyFlags.Typosquat).Should().BeFalse();
    }

    [Fact]
    public void EvaluateFlagsNewMaintainerThisVersion()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        DateTime now = DateTime.UtcNow;

        PackageMeta current = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "some-pkg",
            Version = "2.0.0",
            PublishedAt = now.AddDays(-60),
            MaintainersJson = "[\"alice\", \"mallory\"]",
            FetchedAt = now,
        };
        PackageMeta prior = new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = Ecosystem.Npm,
            Name = "some-pkg",
            Version = "1.0.0",
            PublishedAt = now.AddDays(-120),
            MaintainersJson = "[\"alice\"]",
            FetchedAt = now,
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "some-pkg",
            "2.0.0",
            current,
            prior,
            now,
            NoPopularNames
        );

        flags.HasFlag(AnomalyFlags.NewMaintainerThisVersion).Should().BeTrue();
    }

    [Fact]
    public void EvaluateFlagsScopeMismatchForSquattedScopedPackage()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        IReadOnlySet<string> popular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lodash",
        };

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "@lodash/lodash",
            "1.0.0",
            current: null,
            priorVersionMeta: null,
            DateTime.UtcNow,
            popular
        );

        flags.HasFlag(AnomalyFlags.HighScopeMismatch).Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeReturnsZeroForFirstSnapshot()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        Source source = new()
        {
            Type = SourceType.LocalFolder,
            Name = "first-scan-source",
            ConfigJson = "{\"path\":\"/tmp\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        Guid snapshotId = Guid.NewGuid();
        db.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = source.Id,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "sha",
                ItemCount = 1,
            }
        );
        db.InventoryItems.Add(
            new()
            {
                SnapshotId = snapshotId,
                Ecosystem = Ecosystem.Npm,
                Name = "lodahs",
                Version = "1.0.0",
                IsDirect = true,
            }
        );
        await db.SaveChangesAsync();

        int synthesised = await detector.AnalyzeNewSnapshotAsync(
            source.Id,
            snapshotId,
            CancellationToken.None
        );
        synthesised.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeEmitsSyntheticAdvisoryForTyposquatAddition()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        DateTime now = DateTime.UtcNow;

        // Seed PackageMetas: one popular reference (lodash, 1B downloads) + one suspect
        // candidate (lodahs, 30 downloads, brand new, single maintainer). The detector
        // loads its popular set from this table at runtime.
        feedsDb.PackageMetas.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.21",
                PublishedAt = now.AddYears(-2),
                MaintainersJson = "[\"jdalton\", \"mathias\"]",
                WeeklyDownloads = 1_000_000_000,
                FetchedAt = now,
            }
        );
        feedsDb.PackageMetas.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Ecosystem = Ecosystem.Npm,
                Name = "lodahs",
                Version = "1.0.0",
                PublishedAt = now.AddDays(-3),
                MaintainersJson = "[\"mallory\"]",
                WeeklyDownloads = 30,
                FetchedAt = now,
            }
        );
        await feedsDb.SaveChangesAsync();

        Source source = new()
        {
            Type = SourceType.LocalFolder,
            Name = "anomaly-second-scan",
            ConfigJson = "{\"path\":\"/tmp\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        shieldDb.Sources.Add(source);
        await shieldDb.SaveChangesAsync();

        // Older snapshot — just lodash, no anomaly possible.
        Guid olderId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = olderId,
                SourceId = source.Id,
                TakenAt = DateTime.UtcNow.AddHours(-1),
                ContentsSha = "sha-older",
                ItemCount = 1,
            }
        );
        shieldDb.InventoryItems.Add(
            new()
            {
                SnapshotId = olderId,
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.21",
                IsDirect = true,
            }
        );

        // Newer snapshot — lodash unchanged + new "lodahs" typosquat.
        Guid newerId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = newerId,
                SourceId = source.Id,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "sha-newer",
                ItemCount = 2,
            }
        );
        shieldDb.InventoryItems.Add(
            new()
            {
                SnapshotId = newerId,
                Ecosystem = Ecosystem.Npm,
                Name = "lodash",
                Version = "4.17.21",
                IsDirect = true,
            }
        );
        shieldDb.InventoryItems.Add(
            new()
            {
                SnapshotId = newerId,
                Ecosystem = Ecosystem.Npm,
                Name = "lodahs",
                Version = "1.0.0",
                IsDirect = false,
            }
        );
        await shieldDb.SaveChangesAsync();

        int synthesised = await detector.AnalyzeNewSnapshotAsync(
            source.Id,
            newerId,
            CancellationToken.None
        );

        synthesised.Should().BeGreaterThan(0);

        List<Advisory> advisories = await feedsDb
            .Advisories.Where(advisory =>
                advisory.PackageName == "lodahs" && advisory.Feed == Feed.NpmRegistry
            )
            .ToListAsync();
        advisories.Should().NotBeEmpty();
        advisories.Should().Contain(advisory => advisory.ExternalId.Contains("Typosquat"));
        advisories
            .Should()
            .OnlyContain(advisory =>
                advisory.Severity == Severity.High
                || advisory.Severity == Severity.Medium
                || advisory.Severity == Severity.Low
            );

        // AffectedRangesJson must be a narrow range pinning the exact added version.
        Advisory typosquat = advisories.First(advisory =>
            advisory.ExternalId.Contains("Typosquat")
        );
        using JsonDocument document = JsonDocument.Parse(typosquat.AffectedRangesJson);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        JsonElement events = document.RootElement[0].GetProperty("events");
        events.GetArrayLength().Should().Be(2);
        events[0].GetProperty("introduced").GetString().Should().Be("1.0.0");
        events[1].GetProperty("fixed").GetString().Should().Be("1.0.0-shield-anomaly+1");
    }
}
