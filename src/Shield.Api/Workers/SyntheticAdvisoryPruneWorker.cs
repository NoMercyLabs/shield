namespace Shield.Api.Workers;

// Periodic prune for synthetic advisories whose package no longer appears in any source's
// inventory. Synthetic advisories (Feed.NpmRegistry + ExternalId starting with "anomaly:")
// aren't tied to a Source FK, so the cascade migration that cleans Findings + Inventory
// when a Source is deleted leaves the synthetic Advisory rows behind. Without this worker
// they accumulate forever, polluting the Findings table and re-matching when an inventory
// item with the same name reappears in any source.
//
// Runs every 6 hours; runs once 5 minutes after startup so the initial migration / first
// scan have a chance to settle. Cross-DB orphan check is done in two steps because
// Advisories live in FeedsDbContext and InventoryItems live in ShieldDbContext.
public sealed class SyntheticAdvisoryPruneWorker : BackgroundService
{
    private const string SyntheticExternalIdPrefix = "anomaly:";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyntheticAdvisoryPruneWorker> _log;

    public SyntheticAdvisoryPruneWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SyntheticAdvisoryPruneWorker> log
    )
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Synthetic advisory prune cycle failed");
            }

            try
            {
                await Task.Delay(PruneInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        // Set of (ecosystem, normalised-name) currently in any inventory across any source.
        // Names normalised to lowercase invariant so case drift between the registry's
        // canonical casing and the lockfile's casing doesn't mark live packages orphan.
        HashSet<(Ecosystem, string)> liveKeys = (
            await shieldDb
                .InventoryItems.AsNoTracking()
                .Select(item => new { item.Ecosystem, item.Name })
                .Distinct()
                .ToListAsync(ct)
        )
            .Select(item => (item.Ecosystem, item.Name.ToLowerInvariant()))
            .ToHashSet();

        List<Advisory> synthetics = await feedsDb
            .Advisories.Where(advisory => advisory.ExternalId.StartsWith(SyntheticExternalIdPrefix))
            .ToListAsync(ct);

        List<Guid> orphanIds = synthetics
            .Where(advisory =>
                !liveKeys.Contains((advisory.Ecosystem, advisory.PackageName.ToLowerInvariant()))
            )
            .Select(advisory => advisory.Id)
            .ToList();

        if (orphanIds.Count == 0)
            return;

        // Findings reference Advisory.Id via AdvisoryRefId but the FK lives in ShieldDb
        // while the Advisory lives in FeedsDb — no cascade across contexts. Clean Findings
        // first, then the Advisory rows themselves.
        int findingsRemoved = await shieldDb
            .Findings.Where(finding => orphanIds.Contains(finding.AdvisoryRefId))
            .ExecuteDeleteAsync(ct);

        int advisoriesRemoved = await feedsDb
            .Advisories.Where(advisory => orphanIds.Contains(advisory.Id))
            .ExecuteDeleteAsync(ct);

        _log.LogInformation(
            "Pruned {Advisories} orphan synthetic advisories + {Findings} dependent findings",
            advisoriesRemoved,
            findingsRemoved
        );
    }
}
