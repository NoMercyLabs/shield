using Microsoft.EntityFrameworkCore;
using Shield.Api.Services;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using Shield.Scanners;

namespace Shield.Api.Workers;

public sealed class SourceScanWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScanQueue _scanQueue;
    private readonly MatchQueue _matchQueue;
    private readonly ILogger<SourceScanWorker> _log;

    public SourceScanWorker(
        IServiceScopeFactory scopeFactory,
        ScanQueue scanQueue,
        MatchQueue matchQueue,
        ILogger<SourceScanWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _scanQueue = scanQueue;
        _matchQueue = matchQueue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task ondemand = DrainOnDemandAsync(stoppingToken);
        Task scheduled = RunScheduledAsync(stoppingToken);
        await Task.WhenAll(ondemand, scheduled);
    }

    private async Task DrainOnDemandAsync(CancellationToken stoppingToken)
    {
        await foreach (int sourceId in _scanQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ScanOneAsync(sourceId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Source scan failed for source {SourceId}", sourceId);
            }
        }
    }

    private async Task RunScheduledAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<int> dueIds = await GetDueSourceIdsAsync(stoppingToken);
                foreach (int id in dueIds)
                    await _scanQueue.EnqueueAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Source scan scheduler loop failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<List<int>> GetDueSourceIdsAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        DateTime now = DateTime.UtcNow;

        List<Source> candidates = await db.Sources.Where(source => source.Enabled).ToListAsync(ct);

        return candidates
            .Where(source =>
                source.ScanInterval > TimeSpan.Zero
                && (
                    source.LastScannedAt is null
                    || source.LastScannedAt.Value + source.ScanInterval <= now
                )
            )
            .Select(source => source.Id)
            .ToList();
    }

    private async Task ScanOneAsync(int sourceId, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        ScannerRegistry registry = scope.ServiceProvider.GetRequiredService<ScannerRegistry>();

        Source? source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null)
        {
            _log.LogWarning("Scan requested for missing source {SourceId}", sourceId);
            return;
        }

        IScanner? scanner = registry.FindFor(source.Type);
        if (scanner is null)
        {
            source.LastError = $"No scanner registered for {source.Type}";
            await db.SaveChangesAsync(ct);
            return;
        }

        ScanResult result = await scanner.ScanAsync(source, ct);
        source.LastScannedAt = DateTime.UtcNow;
        source.UpdatedAt = DateTime.UtcNow;

        if (!result.Success || result.Snapshot is null)
        {
            source.LastError = result.Error ?? "Scan failed";
            await db.SaveChangesAsync(ct);
            return;
        }

        source.LastError = null;
        db.InventorySnapshots.Add(result.Snapshot);
        if (result.Items.Count > 0)
            db.InventoryItems.AddRange(result.Items);

        await db.SaveChangesAsync(ct);

        await _matchQueue.EnqueueAsync(
            new MatchRequest(result.Snapshot.Id, source.Id, MatchAll: false),
            ct
        );

        IAnomalyDetector anomalyDetector =
            scope.ServiceProvider.GetRequiredService<IAnomalyDetector>();
        await anomalyDetector.AnalyzeNewSnapshotAsync(source.Id, result.Snapshot.Id, ct);
    }
}
