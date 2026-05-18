using Shield.Api.Services.Updates;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Workers;

// Drains UpdateApplyQueue serially. Each job:
//   1. Broadcasts job.started
//   2. Calls IUpdateApplier.ApplyAsync, streaming per-source outcomes back over SignalR
//   3. Broadcasts job.completed + writes inbox notification
// Failures surface as job.failed + a high-severity notification.
public sealed class UpdateApplyWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UpdateApplyQueue _queue;
    private readonly ILogger<UpdateApplyWorker> _log;

    public UpdateApplyWorker(
        IServiceScopeFactory scopeFactory,
        UpdateApplyQueue queue,
        ILogger<UpdateApplyWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (UpdateApplyJob job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Update apply job {JobId} failed", job.JobId);
                using IServiceScope failScope = _scopeFactory.CreateScope();
                IUpdateApplyBroadcaster failBroadcaster =
                    failScope.ServiceProvider.GetRequiredService<IUpdateApplyBroadcaster>();
                await failBroadcaster.JobFailedAsync(
                    job.JobId,
                    job.RequestedByUserId,
                    ex.Message,
                    stoppingToken
                );
            }
        }
    }

    private async Task ProcessAsync(UpdateApplyJob job, CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IUpdateApplier applier = scope.ServiceProvider.GetRequiredService<IUpdateApplier>();
        IUpdateApplyBroadcaster broadcaster =
            scope.ServiceProvider.GetRequiredService<IUpdateApplyBroadcaster>();

        UpdateApplyRequest request = new(
            Scope: job.Scope,
            SourceIds: job.SourceIds,
            DryRun: false,
            Force: job.Force,
            ConfirmProduction: job.ConfirmProduction
        );

        await broadcaster.JobStartedAsync(job.JobId, totalSources: job.SourceIds?.Count ?? 0, ct);

        UpdateApplyResult result = await applier.ApplyAsync(
            request,
            onSourceCompleted: outcome => broadcaster.SourceCompletedAsync(job.JobId, outcome, ct),
            ct
        );

        await broadcaster.JobCompletedAsync(job.JobId, job.RequestedByUserId, result, ct);
    }
}
