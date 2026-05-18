using Microsoft.AspNetCore.SignalR;
using Shield.Api.Hubs;

namespace Shield.Api.Services.Findings;

public sealed class FindingsBroadcaster : IFindingsBroadcaster
{
    private readonly IHubContext<FindingsHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;

    public FindingsBroadcaster(IHubContext<FindingsHub> hub, IServiceScopeFactory scopeFactory)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    public async Task PublishNewAsync(IReadOnlyList<Finding> findings, CancellationToken ct)
    {
        if (findings.Count == 0)
            return;

        IReadOnlyList<FindingPushPayload> payload = await EnrichAsync(findings, ct);
        await _hub.Clients.All.SendAsync("findings.new", payload, ct);
    }

    public Task PublishCountsAsync(
        int low,
        int medium,
        int high,
        int critical,
        CancellationToken ct
    ) =>
        _hub.Clients.All.SendAsync(
            "findings.counts",
            new CountsPayload(low, medium, high, critical),
            ct
        );

    private async Task<IReadOnlyList<FindingPushPayload>> EnrichAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        HashSet<int> sourceIds = findings.Select(finding => finding.SourceId).ToHashSet();
        HashSet<int> itemIds = findings.Select(finding => finding.InventoryItemId).ToHashSet();
        HashSet<Guid> advisoryIds = findings.Select(finding => finding.AdvisoryRefId).ToHashSet();

        Dictionary<int, string> sourceNames = await shieldDb
            .Sources.Where(source => sourceIds.Contains(source.Id))
            .ToDictionaryAsync(source => source.Id, source => source.Name, ct);

        Dictionary<int, InventoryItem> items = await shieldDb
            .InventoryItems.Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        Dictionary<Guid, Advisory> advisories = await feedsDb
            .Advisories.Where(advisory => advisoryIds.Contains(advisory.Id))
            .ToDictionaryAsync(advisory => advisory.Id, ct);

        List<FindingPushPayload> result = new(findings.Count);
        foreach (Finding finding in findings)
        {
            items.TryGetValue(finding.InventoryItemId, out InventoryItem? item);
            advisories.TryGetValue(finding.AdvisoryRefId, out Advisory? advisory);
            sourceNames.TryGetValue(finding.SourceId, out string? sourceName);
            result.Add(
                new(
                    finding.Id,
                    finding.Severity,
                    item?.Name ?? advisory?.PackageName,
                    item?.Version,
                    advisory?.Summary,
                    sourceName
                )
            );
        }
        return result;
    }

    private sealed record FindingPushPayload(
        Guid Id,
        Severity Severity,
        string? PackageName,
        string? PackageVersion,
        string? AdvisorySummary,
        string? SourceName
    );

    private sealed record CountsPayload(int Low, int Medium, int High, int Critical);
}
