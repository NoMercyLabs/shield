using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// End-to-end queue drain: rows enqueued via IPersistentScanQueue + the FakeScanner registered
// in ShieldWebAppFactory complete via the ScanQueueWorker hosted background service.
public sealed class ScanQueueTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public ScanQueueTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Worker_drains_three_enqueued_rows_to_completion()
    {
        // Force host start so the hosted ScanQueueWorker is actually pumping.
        _ = _factory.CreateClient();

        // Three distinct sources so the per-source serialisation gate doesn't fold them.
        int[] sourceIds = await SeedSourcesAsync(3);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IPersistentScanQueue queue =
                scope.ServiceProvider.GetRequiredService<IPersistentScanQueue>();
            await queue.EnqueueManyAsync(sourceIds);
        }

        await WaitForCompletionAsync(sourceIds, expectFailures: false);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            List<ScanQueueEntry> entries = await db
                .ScanQueueEntries.Where(entry => sourceIds.Contains(entry.SourceId))
                .ToListAsync();
            entries.Should().HaveCount(3);
            entries.Should().OnlyContain(entry => entry.CompletedAt != null);
            entries.Should().OnlyContain(entry => entry.ErrorMessage == null);
            entries.Should().OnlyContain(entry => entry.Attempts == 1);

            // Inventory snapshots written by the FakeScanner.
            List<InventorySnapshot> snapshots = await db
                .InventorySnapshots.Where(snapshot => sourceIds.Contains(snapshot.SourceId))
                .ToListAsync();
            snapshots.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Worker_records_error_when_scanner_throws()
    {
        _ = _factory.CreateClient();

        // Use the GithubRepo type — there's no scanner registered for it in the test factory
        // (only FakeScanner serves LocalFolder), so registry.FindFor returns null and the
        // worker marks the row failed.
        int sourceId = await SeedGithubLikeSourceAsync();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IPersistentScanQueue queue =
                scope.ServiceProvider.GetRequiredService<IPersistentScanQueue>();
            await queue.EnqueueAsync(sourceId);
        }

        await WaitForCompletionAsync([sourceId], expectFailures: true);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            ScanQueueEntry entry = await db.ScanQueueEntries.SingleAsync(row =>
                row.SourceId == sourceId
            );
            entry.CompletedAt.Should().NotBeNull();
            entry.ErrorMessage.Should().NotBeNullOrEmpty();
            entry.Attempts.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task Status_endpoint_reports_recent_failures()
    {
        HttpClient client = _factory.CreateClient();

        int sourceId = await SeedGithubLikeSourceAsync();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IPersistentScanQueue queue =
                scope.ServiceProvider.GetRequiredService<IPersistentScanQueue>();
            await queue.EnqueueAsync(sourceId);
        }

        await WaitForCompletionAsync([sourceId], expectFailures: true);

        ScanQueueStatusResponse? status = await client.GetFromJsonAsync<ScanQueueStatusResponse>(
            "/api/scan-queue"
        );
        status.Should().NotBeNull();
        status!.RecentFailures.Should().Contain(item => item.SourceId == sourceId);
    }

    private async Task<int[]> SeedSourcesAsync(int count)
    {
        List<int> ids = [];
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        for (int i = 0; i < count; i++)
        {
            Source source = new()
            {
                Type = SourceType.LocalFolder,
                Name = $"queue-fixture-{Guid.NewGuid():n}",
                ConfigJson = "{\"path\":\"/tmp\"}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync();
            ids.Add(source.Id);
        }
        return ids.ToArray();
    }

    private async Task<int> SeedGithubLikeSourceAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source source = new()
        {
            Type = SourceType.GithubRepo,
            Name = $"queue-failure-{Guid.NewGuid():n}",
            ConfigJson = "{\"owner\":\"o\",\"repo\":\"r\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task WaitForCompletionAsync(IReadOnlyList<int> sourceIds, bool expectFailures)
    {
        // Worker polls every 2s; give it generous slack for slow CI hosts. 20s ceiling so a
        // legitimately stuck worker shows up as a test failure rather than hanging the suite.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            int outstanding = await db.ScanQueueEntries.CountAsync(entry =>
                sourceIds.Contains(entry.SourceId) && entry.CompletedAt == null
            );
            if (outstanding == 0)
                return;
            await Task.Delay(500);
        }

        // Final assertion so failure messages show the actual remaining-rows state.
        using IServiceScope finalScope = _factory.Services.CreateScope();
        ShieldDbContext finalDb = finalScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        List<ScanQueueEntry> remaining = await finalDb
            .ScanQueueEntries.Where(entry =>
                sourceIds.Contains(entry.SourceId) && entry.CompletedAt == null
            )
            .ToListAsync();
        remaining
            .Should()
            .BeEmpty(
                "expected the worker to drain queue entries within 20s "
                    + $"(expectFailures={expectFailures})"
            );
    }
}
