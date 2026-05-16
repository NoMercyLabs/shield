using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;

namespace Shield.Api.Workers;

public sealed class FeedSyncWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FeedRefreshQueue _refreshQueue;
    private readonly MatchQueue _matchQueue;
    private readonly ILogger<FeedSyncWorker> _log;

    public FeedSyncWorker(
        IServiceScopeFactory scopeFactory,
        FeedRefreshQueue refreshQueue,
        MatchQueue matchQueue,
        ILogger<FeedSyncWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _refreshQueue = refreshQueue;
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
        await foreach (string feedName in _refreshQueue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!Enum.TryParse(feedName, ignoreCase: true, out Feed feed))
            {
                _log.LogWarning("Unknown feed name requested: {Feed}", feedName);
                continue;
            }

            try
            {
                await SyncOneAsync(feed, force: true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Feed refresh failed for {Feed}", feed);
            }
        }
    }

    private async Task RunScheduledAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<Feed> dueFeeds = await GetDueFeedsAsync(stoppingToken);
                foreach (Feed feed in dueFeeds)
                    await SyncOneAsync(feed, force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Feed sync scheduler loop failed");
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

    private async Task<List<Feed>> GetDueFeedsAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IEnumerable<IFeedSync> syncs = scope.ServiceProvider.GetServices<IFeedSync>();
        FeedsDbContext db = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        DateTime now = DateTime.UtcNow;

        List<FeedSyncState> states = await db.FeedSyncStates.ToListAsync(ct);
        Dictionary<Feed, FeedSyncState> stateByFeed = states.ToDictionary(state => state.Feed);

        List<Feed> due = new();
        foreach (IFeedSync sync in syncs)
        {
            if (!stateByFeed.TryGetValue(sync.Feed, out FeedSyncState? state))
            {
                due.Add(sync.Feed);
                continue;
            }
            if (state.NextRunAt <= now)
                due.Add(sync.Feed);
        }
        return due;
    }

    private async Task SyncOneAsync(Feed feed, bool force, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IEnumerable<IFeedSync> syncs = scope.ServiceProvider.GetServices<IFeedSync>();
        IFeedSync? sync = syncs.FirstOrDefault(item => item.Feed == feed);
        if (sync is null)
        {
            _log.LogWarning("No IFeedSync registered for {Feed}", feed);
            return;
        }

        FeedsDbContext db = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        FeedSyncState state =
            await db.FeedSyncStates.FirstOrDefaultAsync(item => item.Feed == feed, ct)
            ?? new FeedSyncState
            {
                Id = Guid.NewGuid(),
                Feed = feed,
                NextRunAt = DateTime.UtcNow,
            };

        bool isNew = state.Id == default || !db.FeedSyncStates.Local.Contains(state);

        if (!force && state.NextRunAt > DateTime.UtcNow)
            return;

        FeedSyncResult result = await sync.SyncAsync(state, ct);
        DateTime now = DateTime.UtcNow;

        state.LastSuccessAt = result.Success ? now : state.LastSuccessAt;
        state.LastError = result.Success ? null : result.Error;
        state.Cursor = result.NextCursor ?? state.Cursor;
        state.NextRunAt = now + TimeSpan.FromMinutes(15);

        if (isNew && await db.FeedSyncStates.FindAsync(new object?[] { state.Id }, ct) is null)
            db.FeedSyncStates.Add(state);

        await db.SaveChangesAsync(ct);

        if (result.Success && result.AdvisoriesIngested + result.AdvisoriesUpdated > 0)
            await _matchQueue.EnqueueAsync(new MatchRequest(null, null, MatchAll: true), ct);
    }
}
