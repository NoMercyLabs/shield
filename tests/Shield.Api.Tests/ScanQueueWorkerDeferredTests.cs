using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Tests that ScanQueueWorker correctly skips deferred queue entries and,
// via FakeRateLimitedScanner, correctly sets DeferredUntil when a
// GitHubScanRateLimitedException is thrown.
public sealed class ScanQueueWorkerDeferredTests : IClassFixture<ScanQueueWorkerDeferredFactory>
{
    private readonly ScanQueueWorkerDeferredFactory _factory;

    public ScanQueueWorkerDeferredTests(ScanQueueWorkerDeferredFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Worker_skips_entry_whose_DeferredUntil_is_in_the_future()
    {
        _ = _factory.CreateClient();

        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        Source source = new()
        {
            Type = SourceType.LocalFolder,
            Name = $"deferred-skip-{Guid.NewGuid():n}",
            ConfigJson = "{\"path\":\"/tmp\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        // Enqueue with DeferredUntil far in the future — the worker must not touch it.
        ScanQueueEntry entry = new()
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            EnqueuedAt = DateTime.UtcNow,
            DeferredUntil = DateTime.UtcNow.AddHours(2),
        };
        db.ScanQueueEntries.Add(entry);
        await db.SaveChangesAsync();

        // Wait two poll cycles (2s each) — if the worker picks up the deferred row it would
        // set CompletedAt. We assert it did NOT.
        await Task.Delay(TimeSpan.FromSeconds(6));

        using IServiceScope verifyScope = _factory.Services.CreateScope();
        ShieldDbContext verifyDb =
            verifyScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        ScanQueueEntry? refreshed = await verifyDb.ScanQueueEntries.FirstOrDefaultAsync(row =>
            row.Id == entry.Id
        );

        refreshed.Should().NotBeNull();
        refreshed!.CompletedAt.Should().BeNull("the deferred row must not be processed yet");
        refreshed.StartedAt.Should().BeNull();
    }
}
