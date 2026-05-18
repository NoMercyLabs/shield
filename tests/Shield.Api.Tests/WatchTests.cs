using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class WatchTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public WatchTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateThenListReturnsTheWatch()
    {
        HttpClient client = _factory.CreateClient();
        string packageName = "lodash-" + Guid.NewGuid().ToString("n");

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/watch",
            new CreateWatchRequest(Ecosystem.Npm, packageName)
        );
        created.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage list = await client.GetAsync("/api/watch");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        List<PackageWatchResponse>? rows = await list.Content.ReadFromJsonAsync<
            List<PackageWatchResponse>
        >();
        rows.Should()
            .Contain(row => row.Ecosystem == Ecosystem.Npm && row.PackageName == packageName);
    }

    [Fact]
    public async Task DuplicateCreateIsIdempotent()
    {
        HttpClient client = _factory.CreateClient();
        string packageName = "axios-" + Guid.NewGuid().ToString("n");

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/watch",
            new CreateWatchRequest(Ecosystem.Npm, packageName)
        );
        PackageWatchResponse? firstBody =
            await first.Content.ReadFromJsonAsync<PackageWatchResponse>();

        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/watch",
            new CreateWatchRequest(Ecosystem.Npm, packageName)
        );
        PackageWatchResponse? secondBody =
            await second.Content.ReadFromJsonAsync<PackageWatchResponse>();

        secondBody!.Id.Should().Be(firstBody!.Id);
    }

    [Fact]
    public async Task DeleteRemovesTheWatch()
    {
        HttpClient client = _factory.CreateClient();
        string packageName = "to-delete-" + Guid.NewGuid().ToString("n");

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/watch",
            new CreateWatchRequest(Ecosystem.Npm, packageName)
        );
        PackageWatchResponse? row = await created.Content.ReadFromJsonAsync<PackageWatchResponse>();

        HttpResponseMessage delete = await client.DeleteAsync($"/api/watch/{row!.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage list = await client.GetAsync("/api/watch");
        List<PackageWatchResponse>? rows = await list.Content.ReadFromJsonAsync<
            List<PackageWatchResponse>
        >();
        rows.Should().NotContain(item => item.Id == row.Id);
    }

    [Fact]
    public async Task SummaryAggregatesOpenFindingsBySeverity()
    {
        string packageName = "summary-pkg-" + Guid.NewGuid().ToString("n");
        await SeedFindingForPackageAsync(packageName, Severity.Critical);
        await SeedFindingForPackageAsync(packageName, Severity.High);
        await SeedFindingForPackageAsync(packageName, Severity.High);

        HttpClient client = _factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/watch",
            new CreateWatchRequest(Ecosystem.Npm, packageName)
        );

        HttpResponseMessage summary = await client.GetAsync("/api/watch/summary");
        summary.StatusCode.Should().Be(HttpStatusCode.OK);
        List<WatchSummaryRow>? rows = await summary.Content.ReadFromJsonAsync<
            List<WatchSummaryRow>
        >();
        WatchSummaryRow? match = rows!.FirstOrDefault(row => row.PackageName == packageName);
        match.Should().NotBeNull();
        match!.SourceCount.Should().BeGreaterThanOrEqualTo(1);
        match.OpenFindings.Critical.Should().Be(1);
        match.OpenFindings.High.Should().Be(2);
    }

    private async Task SeedFindingForPackageAsync(string packageName, Severity severity)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        Source source = new()
        {
            Type = SourceType.LocalFolder,
            Name = "watch-fixture-" + Guid.NewGuid().ToString("n"),
            ConfigJson = JsonSerializer.Serialize(new { path = "/tmp/fixture" }),
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
                ContentsSha = "watch-test",
                ItemCount = 1,
            }
        );
        InventoryItem item = new()
        {
            SnapshotId = snapshotId,
            Ecosystem = Ecosystem.Npm,
            Name = packageName,
            Version = "1.0.0",
            IsDirect = true,
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();

        db.Findings.Add(
            new()
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                InventoryItemId = item.Id,
                AdvisoryRefId = Guid.NewGuid(),
                Severity = severity,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                State = FindingState.Open,
                DedupKey = "watch-" + Guid.NewGuid().ToString("n"),
            }
        );
        await db.SaveChangesAsync();
    }
}
