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
    public async Task CreateThenListReturnsCreatedSource()
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
    public async Task CreateWithBadConfigReturns400()
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
    public async Task GetReturnsEmptyLatestSnapshotWhenNeverScanned()
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
    public async Task ScanNowReturns202WithQueuedPayloadAndSnapshotAppears()
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
        SourceDetailResponse? detail =
            await detailRes.Content.ReadFromJsonAsync<SourceDetailResponse>();
        detail.Should().NotBeNull();
        detail!.LatestSnapshot.Should().NotBeNull();
        detail.LatestSnapshot!.ItemCount.Should().BeGreaterThan(0);

        // And /snapshots + /snapshots/{id}/items must reflect the items the FakeScanner emits.
        HttpResponseMessage snapsRes = await client.GetAsync(
            $"/api/sources/{created.Id}/snapshots"
        );
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

    [Fact]
    public async Task DetectedRemoteRoundTripsThroughSourceDetail()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "detect-remote-fixture",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        // Mutate the source row directly to mimic what LocalFolderScanner would write —
        // FakeScanner is registered in the API fixture and doesn't perform git detection.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Source row = await db.Sources.FirstAsync(item => item.Id == created!.Id);
            row.DetectedRemote = JsonSerializer.Serialize(
                new
                {
                    host = "github.com",
                    owner = "NoMercyLabs",
                    repo = "shield",
                    remoteUrl = "https://github.com/NoMercyLabs/shield.git",
                    branch = "master",
                }
            );
            await db.SaveChangesAsync();
        }

        HttpResponseMessage get = await client.GetAsync($"/api/sources/{created!.Id}");
        SourceDetailResponse? detail = await get.Content.ReadFromJsonAsync<SourceDetailResponse>();
        detail.Should().NotBeNull();
        detail!.Source.DetectedRemote.Should().NotBeNull();
        detail.Source.DetectedRemote!.Host.Should().Be("github.com");
        detail.Source.DetectedRemote.Owner.Should().Be("NoMercyLabs");
        detail.Source.DetectedRemote.Repo.Should().Be("shield");
    }

    [Fact]
    public async Task PromoteToGithubCreatesSiblingGithubSource()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "promote-fixture",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Source row = await db.Sources.FirstAsync(item => item.Id == created!.Id);
            row.DetectedRemote = JsonSerializer.Serialize(
                new
                {
                    host = "github.com",
                    owner = "NoMercyLabs",
                    repo = "shield",
                    remoteUrl = "https://github.com/NoMercyLabs/shield.git",
                    branch = "master",
                }
            );
            await db.SaveChangesAsync();
        }

        HttpResponseMessage promote = await client.PostAsync(
            $"/api/sources/{created!.Id}/promote-to-github",
            content: null
        );
        promote.StatusCode.Should().Be(HttpStatusCode.Created);

        SourceResponse? sibling = await promote.Content.ReadFromJsonAsync<SourceResponse>();
        sibling.Should().NotBeNull();
        sibling!.Type.Should().Be(SourceType.GithubRepo);
        sibling.Name.Should().Be("promote-fixture (GitHub)");
        sibling.ConfigJson.Should().Contain("NoMercyLabs").And.Contain("shield");

        // Original LocalFolder source must still exist.
        HttpResponseMessage original = await client.GetAsync($"/api/sources/{created.Id}");
        original.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PromoteToGithubRejectsSourceWithoutDetectedRemote()
    {
        HttpClient client = _factory.CreateClient();
        object request = new
        {
            type = (int)SourceType.LocalFolder,
            name = "promote-no-remote",
            configJson = new { path = "/tmp" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/sources", request);
        SourceResponse? created = await create.Content.ReadFromJsonAsync<SourceResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage promote = await client.PostAsync(
            $"/api/sources/{created!.Id}/promote-to-github",
            content: null
        );
        promote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BulkLocalFoldersCreatesAndSkipsExisting()
    {
        HttpClient client = _factory.CreateClient();

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-bulk-tests",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(tempRoot);
        string folderA = Path.Combine(tempRoot, "alpha");
        string folderB = Path.Combine(tempRoot, "bravo");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        try
        {
            // Pre-create one source for folderA so the bulk call must dedupe it.
            object preExisting = new
            {
                type = (int)SourceType.LocalFolder,
                name = "alpha-existing",
                configJson = new { path = folderA },
                scanInterval = "01:00:00",
            };
            HttpResponseMessage seed = await client.PostAsJsonAsync("/api/sources", preExisting);
            seed.StatusCode.Should().Be(HttpStatusCode.Created);

            object bulk = new
            {
                paths = new[] { folderA, folderB, "/this/does/not/exist/anywhere" },
                defaultScanInterval = "06:00:00",
            };
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/sources/bulk-local-folders",
                bulk
            );
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("created").GetInt32().Should().Be(1);
            body.GetProperty("skippedExisting").GetInt32().Should().Be(2);
            body.GetProperty("sources").GetArrayLength().Should().Be(1);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // best-effort.
            }
        }
    }

    [Fact]
    public async Task BulkFromGithubCreatesSourcesAndSkipsExisting()
    {
        HttpClient client = _factory.CreateClient();

        // Pre-seed an existing GithubRepo source with the same name we'll try to add — must be skipped.
        object existing = new
        {
            type = (int)SourceType.GithubRepo,
            name = "octocat/dup",
            configJson = new { owner = "octocat", repo = "dup" },
            scanInterval = "01:00:00",
        };
        HttpResponseMessage seed = await client.PostAsJsonAsync("/api/sources", existing);
        seed.StatusCode.Should().Be(HttpStatusCode.Created);

        object request = new
        {
            selections = new[]
            {
                new
                {
                    owner = "octocat",
                    name = "hello-world",
                    branch = (string?)"main",
                },
                new
                {
                    owner = "octocat",
                    name = "spoon-knife",
                    branch = (string?)null,
                },
                new
                {
                    owner = "octocat",
                    name = "dup",
                    branch = (string?)null,
                },
            },
            defaultScanInterval = "06:00:00",
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/sources/bulk-from-github",
            request
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkFromGithubResponse? body =
            await response.Content.ReadFromJsonAsync<BulkFromGithubResponse>();
        body.Should().NotBeNull();
        body!.Created.Should().Be(2);
        body.SkippedExisting.Should().Be(1);
        body.Sources.Should().HaveCount(2);
        body.Sources.Select(source => source.Name)
            .Should()
            .BeEquivalentTo(new[] { "octocat/hello-world", "octocat/spoon-knife" });
        body.Sources.Should().OnlyContain(source => source.Type == SourceType.GithubRepo);
        body.Sources.Should().OnlyContain(source => source.ScanInterval == TimeSpan.FromHours(6));

        // ConfigJson must NOT contain a token — only owner/repo/branch.
        body.Sources.Should()
            .OnlyContain(source =>
                !source.ConfigJson.Contains("token", StringComparison.OrdinalIgnoreCase)
            );

        // Listing reflects 3 GithubRepo sources total (1 seeded + 2 bulk-added).
        HttpResponseMessage list = await client.GetAsync("/api/sources");
        List<SourceResponse>? all = await list.Content.ReadFromJsonAsync<List<SourceResponse>>();
        all!
            .Count(source => source.Type == SourceType.GithubRepo)
            .Should()
            .BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task BulkFromGithubRejectsEmptySelections()
    {
        HttpClient client = _factory.CreateClient();
        object request = new { selections = Array.Empty<object>() };
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/sources/bulk-from-github",
            request
        );
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
