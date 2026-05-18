using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Api.Tests;

public sealed class FakeScanner : IScanner
{
    public SourceType SourceType => SourceType.LocalFolder;

    public ValueTask<ScanResult> ScanAsync(Source source, CancellationToken ct)
    {
        Guid snapshotId = Guid.NewGuid();
        InventorySnapshot snapshot = new()
        {
            Id = snapshotId,
            SourceId = source.Id,
            TakenAt = DateTime.UtcNow,
            ContentsSha = "fake-sha",
            ItemCount = 1,
        };
        InventoryItem item = new()
        {
            SnapshotId = snapshotId,
            Ecosystem = Ecosystem.Npm,
            Name = "fake-pkg",
            Version = "1.0.0",
            IsDirect = true,
        };
        return ValueTask.FromResult(ScanResult.Ok(snapshot, [item]));
    }
}
