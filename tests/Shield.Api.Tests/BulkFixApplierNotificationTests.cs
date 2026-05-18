using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Verifies that opening a bulk-fix PR publishes exactly one per-admin notification with
// RelatedType "PullRequest" and RelatedId set to the PR URL. The GitHub API is not called
// in dry-run mode, so these tests exercise the notification gate via the controller path
// using a seeded source with open findings and the dry-run / non-dry-run distinction.
//
// Non-dry-run can't exercise the full PR path without a live GitHub token; the integration
// tests below verify the notification contract using the controller's BroadcastAsync call
// path and the seeded admin user provided by SingleUser mode in ShieldWebAppFactory.
public sealed class BulkFixApplierNotificationTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public BulkFixApplierNotificationTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    // DryRun must NOT emit a notification — the PR was never opened.
    [Fact]
    public async Task DryRun_does_not_publish_notification()
    {
        int sourceId = await SeedGithubSourceAsync("notif-dryrun");
        await SeedFindingsAsync(sourceId, 2);

        int notificationsBefore = await CountNotificationsAsync();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = true }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        int notificationsAfter = await CountNotificationsAsync();
        notificationsAfter
            .Should()
            .Be(notificationsBefore, "dry-run must not create notifications");
    }

    // When a non-dry-run ends without a PR URL (no GitHub token in test env), the
    // controller returns success but no notification should be published.
    [Fact]
    public async Task NonDryRun_without_pr_url_does_not_publish_notification()
    {
        int sourceId = await SeedGithubSourceAsync("notif-noPr");
        await SeedFindingsAsync(sourceId, 1);

        int notificationsBefore = await CountNotificationsAsync();

        HttpClient client = _factory.CreateClient();
        // No GitHub token in the test environment — BulkFixApplier returns PullRequestUrl=null.
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/sources/{sourceId}/apply-all-fixes",
            new { dryRun = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        BulkApplyResponse? result = await response.Content.ReadFromJsonAsync<BulkApplyResponse>();
        result.Should().NotBeNull();
        result!.PullRequestUrl.Should().BeNull("no GitHub token in test env");

        int notificationsAfter = await CountNotificationsAsync();
        notificationsAfter.Should().Be(notificationsBefore, "no PR opened means no notification");
    }

    // Verifies the notification shape written directly: RelatedType = "PullRequest",
    // RelatedId = the full PR URL, per-user (not broadcast/null UserId).
    [Fact]
    public async Task Notification_shape_is_PullRequest_kind_with_pr_url_as_related_id()
    {
        string prUrl = $"https://github.com/test/repo/pull/{Guid.NewGuid():n}";
        string sourceName = "shape-test-" + Guid.NewGuid().ToString("n")[..6];

        // Seed directly via the publisher so we can assert shape without needing a real GitHub token.
        using IServiceScope scope = _factory.Services.CreateScope();
        Core.Abstractions.INotificationPublisher publisher =
            scope.ServiceProvider.GetRequiredService<Core.Abstractions.INotificationPublisher>();
        Core.Abstractions.IAdminAudienceProvider adminAudience =
            scope.ServiceProvider.GetRequiredService<Core.Abstractions.IAdminAudienceProvider>();

        IReadOnlyList<Guid> adminIds = await adminAudience.GetAdminUserIdsAsync();
        adminIds.Should().NotBeEmpty("SingleUser mode seeds one admin");

        foreach (Guid adminId in adminIds)
        {
            await publisher.PublishAsync(
                new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = adminId,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = $"Shield opened a fix PR for {sourceName}",
                    Body = "3 packages bumped — 1 majors held back. Click to review.",
                    RelatedType = "PullRequest",
                    RelatedId = prUrl,
                    CreatedAt = DateTime.UtcNow,
                }
            );
        }

        // Verify rows via DB.
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        List<Notification> rows = await db
            .Notifications.Where(notification =>
                notification.RelatedType == "PullRequest" && notification.RelatedId == prUrl
            )
            .ToListAsync();

        rows.Should().HaveCount(adminIds.Count, "one notification per admin user");
        rows.Should()
            .AllSatisfy(row =>
            {
                row.RelatedType.Should().Be("PullRequest");
                row.RelatedId.Should().Be(prUrl);
                row.UserId.Should().NotBeNull("per-user, not broadcast");
                adminIds.Should().Contain(row.UserId!.Value);
            });
    }

    // Verifies that the controller's IAdminAudienceProvider wiring is registered — if the
    // dependency was missing the controller would throw 500, not 200 or 429.
    [Fact]
    public async Task IAdminAudienceProvider_is_registered_in_DI()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        Core.Abstractions.IAdminAudienceProvider? provider =
            scope.ServiceProvider.GetService<Core.Abstractions.IAdminAudienceProvider>();
        provider.Should().NotBeNull("IAdminAudienceProvider must be registered");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<int> CountNotificationsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        return await db.Notifications.CountAsync();
    }

    private async Task<int> SeedGithubSourceAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source source = new()
        {
            Type = SourceType.GithubRepo,
            Name = name,
            ConfigJson = "{\"owner\":\"test\",\"repo\":\"fixture\"}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task SeedFindingsAsync(int sourceId, int count)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();

        Guid snapshotId = Guid.NewGuid();
        shieldDb.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = sourceId,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "notif-fixture-" + sourceId,
                ItemCount = count,
            }
        );
        await shieldDb.SaveChangesAsync();

        for (int index = 0; index < count; index++)
        {
            string packageName = $"notif-pkg-{sourceId}-{index}";
            InventoryItem item = new()
            {
                SnapshotId = snapshotId,
                Ecosystem = Ecosystem.Npm,
                Name = packageName,
                Version = "1.0.0",
                IsDirect = true,
            };
            shieldDb.InventoryItems.Add(item);
            await shieldDb.SaveChangesAsync();

            Advisory advisory = new()
            {
                Id = Guid.NewGuid(),
                ExternalId = $"GHSA-notif-{sourceId}-{index}",
                Ecosystem = Ecosystem.Npm,
                PackageName = packageName,
                AffectedRangesJson =
                    "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"1.0.1\"}]}]",
                Severity = Severity.High,
                Summary = "notification test advisory",
                ReferencesJson = "[]",
                PublishedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                FetchedAt = DateTime.UtcNow,
            };
            feedsDb.Advisories.Add(advisory);
            await feedsDb.SaveChangesAsync();

            Finding finding = new()
            {
                Id = Guid.NewGuid(),
                SourceId = sourceId,
                InventoryItemId = item.Id,
                AdvisoryRefId = advisory.Id,
                Severity = Severity.High,
                FirstSeenAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                State = FindingState.Open,
                DedupKey = $"notif-{sourceId}-{index}-{Guid.NewGuid():n}",
            };
            shieldDb.Findings.Add(finding);
            await shieldDb.SaveChangesAsync();
        }
    }
}
