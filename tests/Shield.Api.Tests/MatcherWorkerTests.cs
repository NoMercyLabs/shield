using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Workers.Queues;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// End-to-end coverage for the OSV-driven matcher pass: 5 inventory items including the
// known-vulnerable lodash@4.17.20 land in the DB, the worker queries a stubbed OSV endpoint
// for CVE-2021-23337 (GHSA-35jh-r3h4-6jhm), upserts the advisory, and produces a Finding
// row with the right severity + dedup key.
public sealed class MatcherWorkerTests : IClassFixture<MatcherWorkerTests.OsvStubbedFactory>
{
    private readonly OsvStubbedFactory _factory;

    public MatcherWorkerTests(OsvStubbedFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WorkerEmitsFindingForLodashViaOSVQueryPath()
    {
        // Force host start so the hosted MatcherWorker is actually pumping.
        _ = _factory.CreateClient();

        Guid snapshotId = Guid.NewGuid();
        const int sourceId = 4242;

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

            shieldDb.Sources.Add(
                new()
                {
                    Id = sourceId,
                    Name = "test-source",
                    Type = SourceType.LocalFolder,
                    ConfigJson = "{}",
                    Enabled = true,
                    ScanInterval = TimeSpan.FromHours(24),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            );

            shieldDb.InventorySnapshots.Add(
                new()
                {
                    Id = snapshotId,
                    SourceId = sourceId,
                    TakenAt = DateTime.UtcNow,
                    ContentsSha = "test-sha",
                    ItemCount = 5,
                }
            );

            shieldDb.InventoryItems.AddRange(
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = "lodash",
                    Version = "4.17.20",
                    ParentChain = "[]",
                    IsDirect = true,
                },
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = "express",
                    Version = "4.18.2",
                    ParentChain = "[]",
                    IsDirect = true,
                },
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = "react",
                    Version = "18.2.0",
                    ParentChain = "[]",
                    IsDirect = true,
                },
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = "typescript",
                    Version = "5.3.3",
                    ParentChain = "[]",
                    IsDirect = false,
                },
                new InventoryItem
                {
                    SnapshotId = snapshotId,
                    Ecosystem = Ecosystem.Npm,
                    Name = "vue",
                    Version = "3.4.0",
                    ParentChain = "[]",
                    IsDirect = false,
                }
            );

            await shieldDb.SaveChangesAsync();
        }

        // Trigger a full-pass match. The worker resolves OsvFeedSync, queries our stubbed
        // OSV endpoint, upserts the returned advisory, then runs the matcher.
        MatchQueue queue = _factory.Services.GetRequiredService<MatchQueue>();
        await queue.EnqueueAsync(new(null, null, MatchAll: true));

        await WaitForFindingAsync(sourceId, timeoutSeconds: 10);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

            // OSV upsert wrote the lodash advisory.
            List<Advisory> osvAdvisories = await feedsDb
                .Advisories.Where(advisory => advisory.Feed == Feed.Osv)
                .ToListAsync();
            osvAdvisories
                .Should()
                .Contain(advisory =>
                    advisory.ExternalId == "GHSA-35jh-r3h4-6jhm" && advisory.PackageName == "lodash"
                );

            // Finding row created with the right shape.
            List<Finding> findings = await shieldDb
                .Findings.Where(finding => finding.SourceId == sourceId)
                .ToListAsync();
            findings.Should().HaveCount(1);

            Finding finding = findings.Single();
            finding.Severity.Should().Be(Severity.High);
            finding.State.Should().Be(FindingState.Open);

            InventoryItem lodash = await shieldDb.InventoryItems.SingleAsync(item =>
                item.Id == finding.InventoryItemId
            );
            lodash.Name.Should().Be("lodash");
            lodash.Version.Should().Be("4.17.20");

            string expectedKey = DedupKey.Compute(
                sourceId,
                Ecosystem.Npm,
                "lodash",
                "GHSA-35jh-r3h4-6jhm"
            );
            finding.DedupKey.Should().Be(expectedKey);
        }
    }

    private async Task WaitForFindingAsync(int sourceId, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            bool hit = await shieldDb.Findings.AnyAsync(finding => finding.SourceId == sourceId);
            if (hit)
                return;
            await Task.Delay(200);
        }
    }

    public sealed class OsvStubbedFactory : ShieldWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services
                    .AddHttpClient("osv")
                    .ConfigurePrimaryHttpMessageHandler(() => new StubbedOsvHandler());
            });
        }
    }

    private sealed class StubbedOsvHandler : HttpMessageHandler
    {
        private const string BatchResponse =
            """{"results":[{"vulns":[{"id":"GHSA-35jh-r3h4-6jhm","modified":"2026-04-01T00:00:00Z"}]},{},{},{},{}]}""";

        private const string VulnResponse = """
            {
              "id": "GHSA-35jh-r3h4-6jhm",
              "summary": "Command Injection in lodash",
              "published": "2026-03-01T00:00:00Z",
              "modified": "2026-04-01T00:00:00Z",
              "affected": [
                {
                  "package": { "name": "lodash", "ecosystem": "npm" },
                  "ranges": [
                    { "type": "SEMVER", "events": [{ "introduced": "0" }, { "fixed": "4.17.21" }] }
                  ]
                }
              ],
              "severity": [
                { "type": "CVSS_V3", "score": "CVSS:3.1/AV:N/AC:L/PR:H/UI:N/S:U/C:H/I:H/A:H/7.2" }
              ],
              "references": [
                { "type": "ADVISORY", "url": "https://github.com/advisories/GHSA-35jh-r3h4-6jhm" }
              ]
            }
            """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.RequestUri!.AbsolutePath switch
            {
                "/v1/querybatch" => BatchResponse,
                string p when p.StartsWith("/v1/vulns/") => VulnResponse,
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
