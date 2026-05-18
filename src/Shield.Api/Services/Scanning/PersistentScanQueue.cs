using Microsoft.EntityFrameworkCore;
using Shield.Api.Workers.Queues;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services.Scanning;

// Persistent enqueue for "scan this source now". One row per request; the ScanQueueWorker
// drains FIFO. Bulk-add hits this in a tight loop, so we expose an enumerable overload that
// batches into a single SaveChanges call instead of N round-trips.
public interface IPersistentScanQueue
{
    Task EnqueueAsync(int sourceId, CancellationToken ct = default);
    Task<int> EnqueueManyAsync(IEnumerable<int> sourceIds, CancellationToken ct = default);
}

public sealed class PersistentScanQueue : IPersistentScanQueue
{
    private readonly ShieldDbContext _db;

    public PersistentScanQueue(ShieldDbContext db)
    {
        _db = db;
    }

    public async Task EnqueueAsync(int sourceId, CancellationToken ct = default)
    {
        // Don't double-queue: if there's already a pending row for this source, skip. An
        // in-flight row is fine — the worker won't pick up two for the same source thanks to
        // the in-flight set, but a fresh pending row would still queue work behind it.
        bool alreadyPending = await _db.ScanQueueEntries.AnyAsync(
            entry =>
                entry.SourceId == sourceId && entry.CompletedAt == null && entry.StartedAt == null,
            ct
        );
        if (alreadyPending)
            return;

        _db.ScanQueueEntries.Add(
            new()
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                EnqueuedAt = DateTime.UtcNow,
            }
        );
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> EnqueueManyAsync(
        IEnumerable<int> sourceIds,
        CancellationToken ct = default
    )
    {
        List<int> list = sourceIds.Distinct().ToList();
        if (list.Count == 0)
            return 0;

        HashSet<int> alreadyPending = (
            await _db
                .ScanQueueEntries.Where(entry =>
                    list.Contains(entry.SourceId)
                    && entry.CompletedAt == null
                    && entry.StartedAt == null
                )
                .Select(entry => entry.SourceId)
                .ToListAsync(ct)
        ).ToHashSet();

        DateTime now = DateTime.UtcNow;
        int created = 0;
        foreach (int id in list)
        {
            if (alreadyPending.Contains(id))
                continue;
            _db.ScanQueueEntries.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    SourceId = id,
                    EnqueuedAt = now,
                }
            );
            created++;
        }
        if (created > 0)
            await _db.SaveChangesAsync(ct);
        return created;
    }
}
