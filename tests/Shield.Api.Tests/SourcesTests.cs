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
        CreateSourceRequest request = new(
            Type: SourceType.LocalFolder,
            Name: "list-fixture",
            ConfigJson: "{\"path\":\"/tmp\"}",
            ScanInterval: TimeSpan.FromHours(1)
        );

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
    public async Task Scan_now_returns_202_and_snapshot_appears()
    {
        HttpClient client = _factory.CreateClient();
        CreateSourceRequest request = new(
            Type: SourceType.LocalFolder,
            Name: "scan-fixture",
            ConfigJson: "{\"path\":\"/tmp\"}",
            ScanInterval: TimeSpan.FromHours(1)
        );

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage scan = await client.PostAsync(
            $"/api/sources/{created!.Id}/scan-now",
            content: null
        );
        scan.StatusCode.Should().Be(HttpStatusCode.Accepted);

        bool foundSnapshot = await WaitForSnapshotAsync(created.Id, TimeSpan.FromSeconds(10));
        foundSnapshot
            .Should()
            .BeTrue("scan-now should drive the SourceScanWorker to persist a snapshot");
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
