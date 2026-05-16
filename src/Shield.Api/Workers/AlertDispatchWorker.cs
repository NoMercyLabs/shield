using Microsoft.EntityFrameworkCore;
using Shield.Alerter;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Workers;

public sealed class AlertDispatchWorker : BackgroundService
{
    private static readonly TimeSpan DrainInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertDispatchWorker> _log;

    public AlertDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<AlertDispatchWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Alert dispatch tick failed");
            }

            try
            {
                await Task.Delay(DrainInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        AlertDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<AlertDispatcher>();

        List<AlertChannel> channels = await db
            .AlertChannels.Where(channel => channel.Enabled)
            .ToListAsync(ct);
        if (channels.Count == 0)
            return;

        HashSet<Guid> alreadyAlerted = await db
            .AlertEvents.Where(evt => evt.Status == AlertStatus.Sent)
            .Select(evt => evt.FindingId)
            .ToHashSetAsync(ct);

        List<Finding> pending = await db
            .Findings.Where(finding => finding.State == FindingState.Open)
            .ToListAsync(ct);

        List<Finding> unsent = pending
            .Where(finding => !alreadyAlerted.Contains(finding.Id))
            .ToList();

        if (unsent.Count == 0)
            return;

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(unsent, channels, ct);

        if (events.Count > 0)
        {
            db.AlertEvents.AddRange(events);
            await db.SaveChangesAsync(ct);
        }
    }
}
