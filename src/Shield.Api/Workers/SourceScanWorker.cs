namespace Shield.Api.Workers;

// Scheduler: discovers due sources by interval and enqueues persistent ScanQueueEntries
// rows. The actual drain lives in ScanQueueWorker so a single code path serialises every
// scan and survives restart. The in-memory ScanQueue channel is retained for any future
// caller that wants fire-and-forget triggering without touching the DB, but no current
// production path uses it.
public sealed class SourceScanWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SourceScanWorker> _log;

    public SourceScanWorker(IServiceScopeFactory scopeFactory, ILogger<SourceScanWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScheduledAsync(stoppingToken);
    }

    private async Task RunScheduledAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<int> dueIds = await GetDueSourceIdsAsync(stoppingToken);
                if (dueIds.Count > 0)
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    IPersistentScanQueue persistent =
                        scope.ServiceProvider.GetRequiredService<IPersistentScanQueue>();
                    await persistent.EnqueueManyAsync(dueIds, stoppingToken);
                }
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
}
