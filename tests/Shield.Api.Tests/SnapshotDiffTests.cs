using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SnapshotDiffTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public SnapshotDiffTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Diff_returns_added_removed_and_version_changed()
    {
        HttpClient client = _factory.CreateClient();
        int sourceId = await SeedSourceAsync(client, "diff-shape-fixture");

        // Older snapshot: lodash + axios + react@18.0.0
        Guid olderId = Guid.NewGuid();
        await SeedSnapshotAsync(
            sourceId,
            olderId,
            takenAt: DateTime.UtcNow.AddHours(-2),
            (Ecosystem.Npm, "lodash", "4.17.21"),
            (Ecosystem.Npm, "axios", "1.6.0"),
            (Ecosystem.Npm, "react", "18.0.0")
        );

        // Newer snapshot: lodash gone, axios bumped, react unchanged, NEW left-pad
        Guid newerId = Guid.NewGuid();
        await SeedSnapshotAsync(
            sourceId,
            newerId,
            takenAt: DateTime.UtcNow.AddMinutes(-30),
            (Ecosystem.Npm, "axios", "1.7.2"),
            (Ecosystem.Npm, "react", "18.0.0"),
            (Ecosystem.Npm, "left-pad", "1.3.0")
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/sources/{sourceId}/snapshots/{olderId}/diff/{newerId}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        SnapshotDiffResponse? diff =
            await response.Content.ReadFromJsonAsync<SnapshotDiffResponse>();
        diff.Should().NotBeNull();
        diff!.Older.Id.Should().Be(olderId);
        diff.Newer.Id.Should().Be(newerId);

        diff.Added.Should().HaveCount(1);
        diff.Added[0].Name.Should().Be("left-pad");
        diff.Added[0].Version.Should().Be("1.3.0");

        diff.Removed.Should().HaveCount(1);
        diff.Removed[0].Name.Should().Be("lodash");

        diff.VersionChanged.Should().HaveCount(1);
        diff.VersionChanged[0].Name.Should().Be("axios");
        diff.VersionChanged[0].FromVersion.Should().Be("1.6.0");
        diff.VersionChanged[0].ToVersion.Should().Be("1.7.2");
    }

    [Fact]
    public async Task Diff_returns_400_when_snapshot_ids_match()
    {
        HttpClient client = _factory.CreateClient();
        int sourceId = await SeedSourceAsync(client, "diff-same-ids");
        Guid snapshotId = Guid.NewGuid();
        await SeedSnapshotAsync(
            sourceId,
            snapshotId,
            takenAt: DateTime.UtcNow,
            (Ecosystem.Npm, "lodash", "4.17.21")
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/sources/{sourceId}/snapshots/{snapshotId}/diff/{snapshotId}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Diff_returns_404_when_snapshot_belongs_to_different_source()
    {
        HttpClient client = _factory.CreateClient();
        int sourceA = await SeedSourceAsync(client, "diff-source-a");
        int sourceB = await SeedSourceAsync(client, "diff-source-b");

        Guid olderId = Guid.NewGuid();
        Guid newerId = Guid.NewGuid();
        await SeedSnapshotAsync(
            sourceA,
            olderId,
            takenAt: DateTime.UtcNow.AddHours(-1),
            (Ecosystem.Npm, "lodash", "4.17.21")
        );
        // newerId belongs to source B — same controller call with source A's id must 404.
        await SeedSnapshotAsync(
            sourceB,
            newerId,
            takenAt: DateTime.UtcNow,
            (Ecosystem.Npm, "lodash", "4.17.22")
        );

        HttpResponseMessage response = await client.GetAsync(
            $"/api/sources/{sourceA}/snapshots/{olderId}/diff/{newerId}"
        );
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Snapshots_list_includes_prev_snapshot_id_chain()
    {
        HttpClient client = _factory.CreateClient();
        int sourceId = await SeedSourceAsync(client, "snapshots-prev-chain");

        Guid oldestId = Guid.NewGuid();
        Guid middleId = Guid.NewGuid();
        Guid newestId = Guid.NewGuid();
        DateTime baseTime = DateTime.UtcNow.AddHours(-3);
        await SeedSnapshotAsync(
            sourceId,
            oldestId,
            takenAt: baseTime,
            (Ecosystem.Npm, "lodash", "4.17.21")
        );
        await SeedSnapshotAsync(
            sourceId,
            middleId,
            takenAt: baseTime.AddHours(1),
            (Ecosystem.Npm, "lodash", "4.17.21")
        );
        await SeedSnapshotAsync(
            sourceId,
            newestId,
            takenAt: baseTime.AddHours(2),
            (Ecosystem.Npm, "lodash", "4.17.21")
        );

        HttpResponseMessage response = await client.GetAsync($"/api/sources/{sourceId}/snapshots");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        List<SnapshotListItem>? snapshots = await response.Content.ReadFromJsonAsync<
            List<SnapshotListItem>
        >();
        snapshots.Should().NotBeNull();
        snapshots!.Should().HaveCount(3);

        // Ordered newest-first; PrevSnapshotId chains backwards in time.
        snapshots[0].Id.Should().Be(newestId);
        snapshots[0].PrevSnapshotId.Should().Be(middleId);
        snapshots[1].Id.Should().Be(middleId);
        snapshots[1].PrevSnapshotId.Should().Be(oldestId);
        snapshots[2].Id.Should().Be(oldestId);
        snapshots[2].PrevSnapshotId.Should().BeNull();
    }

    private async Task<int> SeedSourceAsync(HttpClient client, string name)
    {
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name,
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();
        return created!.Id;
    }

    private async Task SeedSnapshotAsync(
        int sourceId,
        Guid snapshotId,
        DateTime takenAt,
        params (Ecosystem Ecosystem, string Name, string Version)[] items
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        db.InventorySnapshots.Add(
            new InventorySnapshot
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = takenAt,
                ContentsSha = $"sha-{snapshotId:N}",
                ItemCount = items.Length,
            }
        );
        foreach ((Ecosystem ecosystem, string name, string version) in items)
        {
            db.InventoryItems.Add(
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = ecosystem,
                    Name = name,
                    Version = version,
                    IsDirect = true,
                }
            );
        }
        await db.SaveChangesAsync();
    }
}
