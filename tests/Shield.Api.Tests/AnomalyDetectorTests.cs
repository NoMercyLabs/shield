using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Api.Workers;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class AnomalyDetectorTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public AnomalyDetectorTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Evaluate_flags_brand_new_package()
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
            now
        );

        flags.HasFlag(AnomalyFlags.BrandNew).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_does_not_flag_brand_new_for_old_publish_date()
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
            now
        );

        flags.HasFlag(AnomalyFlags.BrandNew).Should().BeFalse();
        flags.HasFlag(AnomalyFlags.SingleMaintainer).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_flags_typosquat_for_close_npm_name()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        // "lodahs" — one transposition away from "lodash".
        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "lodahs",
            "1.0.0",
            current: null,
            priorVersionMeta: null,
            DateTime.UtcNow
        );

        flags.HasFlag(AnomalyFlags.Typosquat).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_does_not_flag_known_popular_package_as_typosquat()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "lodash",
            "4.17.21",
            current: null,
            priorVersionMeta: null,
            DateTime.UtcNow
        );

        flags.HasFlag(AnomalyFlags.Typosquat).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_flags_new_maintainer_this_version()
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
            now
        );

        flags.HasFlag(AnomalyFlags.NewMaintainerThisVersion).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_flags_scope_mismatch_for_squatted_scoped_package()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

        AnomalyFlags flags = detector.Evaluate(
            Ecosystem.Npm,
            "@lodash/lodash",
            "1.0.0",
            current: null,
            priorVersionMeta: null,
            DateTime.UtcNow
        );

        flags.HasFlag(AnomalyFlags.HighScopeMismatch).Should().BeTrue();
    }

    [Fact]
    public async Task Analyze_returns_zero_for_first_snapshot()
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
    public async Task Analyze_emits_synthetic_advisory_for_typosquat_addition()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        IAnomalyDetector detector = scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();

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
