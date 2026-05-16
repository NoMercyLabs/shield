using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shield.Api.Hubs;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Validates the wire payload the FindingsHub will broadcast. Uses NSubstitute on IHubContext
// rather than a real SignalR client connection so the test project doesn't pull a new NuGet —
// the broadcaster is the only thing that ever formats hub messages, so capturing its SendAsync
// call gives the same coverage as round-tripping through the transport.
public sealed class FindingsHubTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public FindingsHubTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishNewAsync_emits_findings_new_with_expected_payload_shape()
    {
        IHubContext<FindingsHub> hubContext = Substitute.For<IHubContext<FindingsHub>>();
        IHubClients clients = Substitute.For<IHubClients>();
        IClientProxy proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(proxy);

        Guid advisoryId = SeedSourceAdvisoryItem(
            sourceId: 4242,
            sourceName: "hub-fixture-source",
            inventoryItemId: 7777,
            packageName: "left-pad",
            packageVersion: "1.3.0",
            advisoryExternalId: "TEST-HUB-PAYLOAD-" + Guid.NewGuid().ToString("n"),
            advisorySummary: "synthetic hub-payload advisory"
        );

        FindingsBroadcaster broadcaster = new(
            hubContext,
            _factory.Services.GetRequiredService<IServiceScopeFactory>()
        );

        Finding finding = new()
        {
            Id = Guid.NewGuid(),
            SourceId = 4242,
            InventoryItemId = 7777,
            AdvisoryRefId = advisoryId,
            Severity = Severity.Critical,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "hub-test-" + Guid.NewGuid().ToString("n"),
        };

        await broadcaster.PublishNewAsync(new[] { finding }, CancellationToken.None);

        await proxy
            .Received(1)
            .SendCoreAsync(
                "findings.new",
                Arg.Is<object?[]>(args => MatchesNewPayload(args, finding.Id)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PublishCountsAsync_emits_findings_counts_with_four_buckets()
    {
        IHubContext<FindingsHub> hubContext = Substitute.For<IHubContext<FindingsHub>>();
        IHubClients clients = Substitute.For<IHubClients>();
        IClientProxy proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(proxy);

        FindingsBroadcaster broadcaster = new(
            hubContext,
            _factory.Services.GetRequiredService<IServiceScopeFactory>()
        );

        await broadcaster.PublishCountsAsync(
            low: 4,
            medium: 3,
            high: 2,
            critical: 1,
            CancellationToken.None
        );

        await proxy
            .Received(1)
            .SendCoreAsync(
                "findings.counts",
                Arg.Is<object?[]>(args => MatchesCountsPayload(args, 4, 3, 2, 1)),
                Arg.Any<CancellationToken>()
            );
    }

    private static bool MatchesNewPayload(object?[] args, Guid expectedId)
    {
        if (args.Length != 1)
            return false;
        if (args[0] is not System.Collections.IEnumerable list)
            return false;
        foreach (object? entry in list)
        {
            if (entry is null)
                return false;
            Type type = entry.GetType();
            object? id = type.GetProperty("Id")?.GetValue(entry);
            if (id is Guid guid && guid == expectedId)
            {
                return type.GetProperty("Severity") is not null
                    && type.GetProperty("PackageName") is not null
                    && type.GetProperty("PackageVersion") is not null
                    && type.GetProperty("AdvisorySummary") is not null
                    && type.GetProperty("SourceName") is not null;
            }
        }
        return false;
    }

    private static bool MatchesCountsPayload(
        object?[] args,
        int low,
        int medium,
        int high,
        int critical
    )
    {
        if (args.Length != 1 || args[0] is null)
            return false;
        Type type = args[0]!.GetType();
        int? Read(string name) => type.GetProperty(name)?.GetValue(args[0]) as int?;
        return Read("Low") == low
            && Read("Medium") == medium
            && Read("High") == high
            && Read("Critical") == critical;
    }

    private Guid SeedSourceAdvisoryItem(
        int sourceId,
        string sourceName,
        int inventoryItemId,
        string packageName,
        string packageVersion,
        string advisoryExternalId,
        string advisorySummary
    )
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        if (!shieldDb.Sources.Any(source => source.Id == sourceId))
        {
            Source source = new()
            {
                Id = sourceId,
                Type = SourceType.LocalFolder,
                Name = sourceName,
                ConfigJson = "{}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            shieldDb.Sources.Add(source);
        }

        if (!shieldDb.InventoryItems.Any(item => item.Id == inventoryItemId))
        {
            Guid snapshotId = Guid.NewGuid();
            InventorySnapshot snapshot = new()
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "hub-fixture",
                ItemCount = 1,
            };
            shieldDb.InventorySnapshots.Add(snapshot);

            InventoryItem item = new()
            {
                Id = inventoryItemId,
                SnapshotId = snapshotId,
                Ecosystem = Ecosystem.Npm,
                Name = packageName,
                Version = packageVersion,
                IsDirect = true,
            };
            shieldDb.InventoryItems.Add(item);
        }
        shieldDb.SaveChanges();

        Advisory advisory = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = advisoryExternalId,
            Ecosystem = Ecosystem.Npm,
            PackageName = packageName,
            AffectedRangesJson = "[]",
            Severity = Severity.Critical,
            Summary = advisorySummary,
            ReferencesJson = "[]",
            PublishedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            FetchedAt = DateTime.UtcNow,
        };
        feedsDb.Advisories.Add(advisory);
        feedsDb.SaveChanges();
        return advisory.Id;
    }
}
