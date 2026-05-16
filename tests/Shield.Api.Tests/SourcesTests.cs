using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SourcesTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public SourcesTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_then_list_returns_created_source()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "list-fixture",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage list = await client.GetAsync("/api/sources");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        List<SourceResponse>? sources = await list.Content.ReadFromJsonAsync<
            List<SourceResponse>
        >();
        sources.Should().NotBeNull();
        sources!.Should().Contain(source => source.Name == "list-fixture");
    }

    [Fact]
    public async Task Create_with_bad_config_returns_400()
    {
        HttpClient client = _factory.CreateClient();
        // LocalFolder requires a 'path' — empty object should fail validation.
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "bad-config",
            configJson = new { },
            scanInterval = "01:00:00",
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_returns_empty_latest_snapshot_when_never_scanned()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "never-scanned",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage get = await client.GetAsync($"/api/sources/{created!.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        SourceDetailResponse? detail = await get.Content.ReadFromJsonAsync<SourceDetailResponse>();
        detail.Should().NotBeNull();
        detail!.Source.Id.Should().Be(created.Id);
        detail.LatestSnapshot.Should().BeNull();
    }

    [Fact]
    public async Task Scan_now_returns_202_with_queued_payload_and_snapshot_appears()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "scan-fixture",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage scan = await client.PostAsync(
            $"/api/sources/{created!.Id}/scan-now",
            content: null
        );
        scan.StatusCode.Should().Be(HttpStatusCode.Accepted);

        ScanQueuedResponse? body = await scan.Content.ReadFromJsonAsync<ScanQueuedResponse>();
        body.Should().NotBeNull();
        body!.Accepted.Should().BeTrue();
        body.QueuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        bool foundSnapshot = await WaitForSnapshotAsync(created.Id, TimeSpan.FromSeconds(10));
        foundSnapshot
            .Should()
            .BeTrue("scan-now should drive the SourceScanWorker to persist a snapshot");

        // After the worker finishes the source detail must surface the latest snapshot.
        HttpResponseMessage detailRes = await client.GetAsync($"/api/sources/{created.Id}");
        SourceDetailResponse? detail = await detailRes.Content.ReadFromJsonAsync<SourceDetailResponse>();
        detail.Should().NotBeNull();
        detail!.LatestSnapshot.Should().NotBeNull();
        detail.LatestSnapshot!.ItemCount.Should().BeGreaterThan(0);

        // And /snapshots + /snapshots/{id}/items must reflect the items the FakeScanner emits.
        HttpResponseMessage snapsRes = await client.GetAsync($"/api/sources/{created.Id}/snapshots");
        snapsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        List<SnapshotListItem>? snapshots = await snapsRes.Content.ReadFromJsonAsync<
            List<SnapshotListItem>
        >();
        snapshots.Should().NotBeNullOrEmpty();

        Guid snapshotId = snapshots![0].Id;
        HttpResponseMessage itemsRes = await client.GetAsync(
            $"/api/sources/{created.Id}/snapshots/{snapshotId}/items"
        );
        itemsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        PagedResponse<InventoryItemResponse>? items = await itemsRes.Content.ReadFromJsonAsync<
            PagedResponse<InventoryItemResponse>
        >();
        items.Should().NotBeNull();
        items!.Items.Should().NotBeEmpty();
    }

    private async Task<bool> WaitForSnapshotAsync(int sourceId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            bool any = await db.InventorySnapshots.AnyAsync(snapshot =>
                snapshot.SourceId == sourceId
            );
            if (any)
                return true;
            await Task.Delay(200);
        }
        return false;
    }
}
