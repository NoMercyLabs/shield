using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Workers;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Integration coverage for MatcherWorker auto-resolve: findings that were Open or Acked
// but whose package no longer matches any advisory transition to AutoResolved.
// Resolved and Suppressed findings are left untouched.
//
// Each test uses a unique (sourceId, packageName) pair to avoid advisory cross-contamination
// in the shared FeedsDb.
public sealed class MatcherAutoResolveTests
    : IClassFixture<MatcherAutoResolveTests.AutoResolveStubbedFactory>
{
    private readonly AutoResolveStubbedFactory _factory;

    public MatcherAutoResolveTests(AutoResolveStubbedFactory factory)
    {
        _factory = factory;
    }

    // Lodash 4.17.20 is vulnerable. Advisory says fixed at 4.17.21.
    // First scan: pkg@4.17.20 → Finding is Open.
    // Second scan: pkg@4.17.22 → no match → Finding becomes AutoResolved.
    [Fact]
    public async Task Open_finding_becomes_AutoResolved_when_version_bumped_past_fix()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9001;
        const string pkg = "ar-test-open";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId, "GHSA-ar-001", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 1, timeoutSeconds: 10);

        // Confirm state is Open before the second scan.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            List<Finding> findings = await shieldDb
                .Findings.Where(f => f.SourceId == sourceId)
                .ToListAsync();
            findings.Should().HaveCount(1);
            findings[0].State.Should().Be(FindingState.Open);
        }

        // Second scan — pkg@4.17.22 is past the fix boundary → no match.
        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, pkg, "4.17.22");
        await TriggerMatchAsync(snapshotV2);
        await WaitForStateAsync(sourceId, FindingState.AutoResolved, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            List<Finding> findings = await shieldDb
                .Findings.Where(f => f.SourceId == sourceId)
                .ToListAsync();
            findings.Should().HaveCount(1);
            findings[0].State.Should().Be(FindingState.AutoResolved);

            // Audit row must have been written.
            bool hasAudit = await shieldDb.AuditEntries.AnyAsync(entry =>
                entry.Action == "finding.auto_resolved"
                && entry.TargetId == findings[0].Id.ToString()
            );
            hasAudit.Should().BeTrue();
        }
    }

    // Same scenario but the finding was Acked.
    // Acked findings are also fair game for auto-resolve.
    [Fact]
    public async Task Acked_finding_becomes_AutoResolved_when_version_bumped_past_fix()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9002;
        const string pkg = "ar-test-acked";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId, "GHSA-ar-002", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 1, timeoutSeconds: 10);

        // Manually set state to Acked before the second scan.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State = FindingState.Acked;
            await shieldDb.SaveChangesAsync();
        }

        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, pkg, "4.17.22");
        await TriggerMatchAsync(snapshotV2);
        await WaitForStateAsync(sourceId, FindingState.AutoResolved, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State.Should().Be(FindingState.AutoResolved);
        }
    }

    // Operator manually resolved the finding. The matcher must not override that decision.
    [Fact]
    public async Task Resolved_finding_stays_Resolved()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9003;
        const string pkg = "ar-test-resolved";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId, "GHSA-ar-003", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 1, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State = FindingState.Resolved;
            await shieldDb.SaveChangesAsync();
        }

        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, pkg, "4.17.22");
        await TriggerMatchAsync(snapshotV2);
        await Task.Delay(2000);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State.Should().Be(FindingState.Resolved);
        }
    }

    // Operator suppressed the finding. The matcher must not override that decision.
    [Fact]
    public async Task Suppressed_finding_stays_Suppressed()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9004;
        const string pkg = "ar-test-suppressed";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId, "GHSA-ar-004", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 1, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State = FindingState.Suppressed;
            await shieldDb.SaveChangesAsync();
        }

        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, pkg, "4.17.22");
        await TriggerMatchAsync(snapshotV2);
        await Task.Delay(2000);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State.Should().Be(FindingState.Suppressed);
        }
    }

    // The package was removed from the manifest entirely (no inventory item for it in the
    // new snapshot). That is a genuine resolution — the dep is gone.
    [Fact]
    public async Task Finding_auto_resolves_when_package_deleted_from_manifest()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9005;
        const string pkg = "ar-test-deleted";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId, "GHSA-ar-005", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 1, timeoutSeconds: 10);

        // New snapshot with a completely different package — the original is gone.
        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, "ar-test-other-pkg", "4.18.2");
        await TriggerMatchAsync(snapshotV2);
        await WaitForStateAsync(sourceId, FindingState.AutoResolved, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            Finding finding = await shieldDb.Findings.SingleAsync(f => f.SourceId == sourceId);
            finding.State.Should().Be(FindingState.AutoResolved);
        }
    }

    // Multiple findings for same package but different advisories — all auto-resolve in one pass.
    [Fact]
    public async Task Multiple_findings_for_same_package_all_auto_resolve()
    {
        _ = _factory.CreateClient();

        const int sourceId = 9006;
        const string pkg = "ar-test-multi";
        Guid snapshotV1 = Guid.NewGuid();
        Guid advisoryId1 = Guid.NewGuid();
        Guid advisoryId2 = Guid.NewGuid();

        await SeedSourceAsync(sourceId);
        await SeedAdvisoryAsync(advisoryId1, "GHSA-ar-006a", pkg);
        await SeedAdvisoryAsync(advisoryId2, "GHSA-ar-006b", pkg);
        await SeedSnapshotAsync(sourceId, snapshotV1, pkg, "4.17.20");
        await TriggerMatchAsync(snapshotV1);
        await WaitForFindingCountAsync(sourceId, expectedCount: 2, timeoutSeconds: 10);

        Guid snapshotV2 = Guid.NewGuid();
        await SeedSnapshotAsync(sourceId, snapshotV2, pkg, "4.17.22");
        await TriggerMatchAsync(snapshotV2);
        await WaitForAllStateAsync(
            sourceId,
            FindingState.AutoResolved,
            expectedCount: 2,
            timeoutSeconds: 10
        );

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            List<Finding> findings = await shieldDb
                .Findings.Where(f => f.SourceId == sourceId)
                .ToListAsync();
            findings.Should().HaveCount(2);
            findings.Should().AllSatisfy(f => f.State.Should().Be(FindingState.AutoResolved));
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task SeedSourceAsync(int sourceId) =>
        WithShieldDbAsync(async shieldDb =>
        {
            bool exists = await shieldDb.Sources.AnyAsync(s => s.Id == sourceId);
            if (exists)
                return;
            shieldDb.Sources.Add(
                new Source
                {
                    Id = sourceId,
                    Name = $"auto-resolve-test-{sourceId}",
                    Type = SourceType.LocalFolder,
                    ConfigJson = "{}",
                    Enabled = true,
                    ScanInterval = TimeSpan.FromHours(24),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            );
            await shieldDb.SaveChangesAsync();
        });

    // Seeds an advisory directly into FeedsDb so the matcher has something to match against
    // without a live OSV network call. Each test uses a unique packageName so advisories
    // don't bleed across tests in the shared FeedsDb.
    private Task SeedAdvisoryAsync(Guid id, string externalId, string packageName) =>
        WithFeedsDbAsync(async feedsDb =>
        {
            bool exists = await feedsDb.Advisories.AnyAsync(a => a.Id == id);
            if (exists)
                return;
            feedsDb.Advisories.Add(
                new Advisory
                {
                    Id = id,
                    Feed = Feed.Osv,
                    ExternalId = externalId,
                    Ecosystem = Ecosystem.Npm,
                    PackageName = packageName,
                    // Vulnerable: 0 ≤ version < 4.17.21
                    AffectedRangesJson =
                        "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]",
                    Severity = Severity.High,
                    Summary = "synthetic advisory",
                    ReferencesJson = "[]",
                    PublishedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    FetchedAt = DateTime.UtcNow,
                }
            );
            await feedsDb.SaveChangesAsync();
        });

    private Task SeedSnapshotAsync(
        int sourceId,
        Guid snapshotId,
        string packageName,
        string version
    ) =>
        WithShieldDbAsync(async shieldDb =>
        {
            InventorySnapshot snapshot = new()
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = DateTime.UtcNow,
                ContentsSha = $"sha-{snapshotId}",
                ItemCount = 1,
            };
            shieldDb.InventorySnapshots.Add(snapshot);

            shieldDb.InventoryItems.Add(
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = packageName,
                    Version = version,
                    ParentChain = "[]",
                    IsDirect = true,
                }
            );
            await shieldDb.SaveChangesAsync();
        });

    private async Task TriggerMatchAsync(Guid snapshotId)
    {
        MatchQueue queue = _factory.Services.GetRequiredService<MatchQueue>();
        await queue.EnqueueAsync(new MatchRequest(snapshotId, null, MatchAll: false));
    }

    private async Task WaitForFindingCountAsync(int sourceId, int expectedCount, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            int count = await shieldDb.Findings.CountAsync(f => f.SourceId == sourceId);
            if (count >= expectedCount)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException(
            $"Expected {expectedCount} finding(s) for source {sourceId} within {timeoutSeconds}s."
        );
    }

    private async Task WaitForStateAsync(
        int sourceId,
        FindingState expectedState,
        int timeoutSeconds
    )
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            bool hit = await shieldDb.Findings.AnyAsync(f =>
                f.SourceId == sourceId && f.State == expectedState
            );
            if (hit)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException(
            $"Expected finding for source {sourceId} to reach state {expectedState} within {timeoutSeconds}s."
        );
    }

    private async Task WaitForAllStateAsync(
        int sourceId,
        FindingState expectedState,
        int expectedCount,
        int timeoutSeconds
    )
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            int count = await shieldDb.Findings.CountAsync(f =>
                f.SourceId == sourceId && f.State == expectedState
            );
            if (count >= expectedCount)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException(
            $"Expected {expectedCount} finding(s) in state {expectedState} for source {sourceId}."
        );
    }

    private async Task WithShieldDbAsync(Func<ShieldDbContext, Task> action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        await action(shieldDb);
    }

    private async Task WithFeedsDbAsync(Func<FeedsDbContext, Task> action)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        await action(feedsDb);
    }

    // Stubs OSV to return empty querybatch results — advisories are seeded directly in FeedsDb.
    public sealed class AutoResolveStubbedFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services
                    .AddHttpClient("osv")
                    .ConfigurePrimaryHttpMessageHandler(() => new EmptyOsvHandler());
            });
        }
    }

    private sealed class EmptyOsvHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/v1/querybatch" => "{\"results\":[]}",
                _ => "{}",
            };
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
