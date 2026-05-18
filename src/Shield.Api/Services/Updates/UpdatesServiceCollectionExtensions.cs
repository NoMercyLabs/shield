using Shield.Api.Workers;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Services.Updates;

// Registers the dependency-updates pipeline:
//   - UpdateScannerWorker sweeps registries daily, fills PackageUpdate rows
//   - UpdateApplyQueue + UpdateApplyWorker drain enqueued apply jobs asynchronously
//   - UpdateApplyBroadcaster fans per-source progress over SignalR + writes inbox notifications
//   - UpdateApplier holds the source-by-source manifest-edit + PR-open logic
public static class UpdatesServiceCollectionExtensions
{
    public static IServiceCollection AddShieldUpdates(this IServiceCollection services)
    {
        // Worker is both singleton (controller calls SweepSourceAsync) and hosted (daily sweep).
        services.AddSingleton<UpdateScannerWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<UpdateScannerWorker>());

        services.AddSingleton<UpdateApplyQueue>();
        services.AddHostedService<UpdateApplyWorker>();
        services.AddScoped<IUpdateApplyBroadcaster, UpdateApplyBroadcaster>();
        services.AddScoped<IUpdateApplier, UpdateApplier>();
        return services;
    }
}
