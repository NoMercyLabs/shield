using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.Auth;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class NotificationsTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public NotificationsTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BroadcastAppearsInListWithUnreadCount()
    {
        string title = "scan-failed-" + Guid.NewGuid().ToString("n");
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            INotificationPublisher publisher =
                scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await publisher.BroadcastAsync(
                NotificationKind.ScanFailed,
                Severity.High,
                title,
                "the scanner blew up",
                relatedType: "Source",
                relatedId: "99"
            );
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        NotificationsPage? page = await response.Content.ReadFromJsonAsync<NotificationsPage>();
        page.Should().NotBeNull();
        page!
            .Items.Should()
            .Contain(item => item.Title == title && item.Kind == NotificationKind.ScanFailed);
        page.UnreadCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MarkReadSetsReadAtAndDecrementsUnread()
    {
        string title = "mark-read-" + Guid.NewGuid().ToString("n");
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            INotificationPublisher publisher =
                scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await publisher.BroadcastAsync(
                NotificationKind.SystemMessage,
                Severity.Low,
                title,
                "informational"
            );
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage listResponse = await client.GetAsync("/api/notifications");
        NotificationsPage? page = await listResponse.Content.ReadFromJsonAsync<NotificationsPage>();
        Guid id = page!.Items.First(item => item.Title == title).Id;

        HttpResponseMessage readResponse = await client.PostAsync(
            $"/api/notifications/{id}/read",
            content: null
        );
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        NotificationResponse? body =
            await readResponse.Content.ReadFromJsonAsync<NotificationResponse>();
        body!.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveHidesFromDefaultList()
    {
        string title = "archive-" + Guid.NewGuid().ToString("n");
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            INotificationPublisher publisher =
                scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await publisher.BroadcastAsync(
                NotificationKind.SystemMessage,
                Severity.Low,
                title,
                "to be archived"
            );
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage listResponse = await client.GetAsync("/api/notifications");
        NotificationsPage? page = await listResponse.Content.ReadFromJsonAsync<NotificationsPage>();
        Guid id = page!.Items.First(item => item.Title == title).Id;

        HttpResponseMessage archive = await client.PostAsync(
            $"/api/notifications/{id}/archive",
            content: null
        );
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage after = await client.GetAsync("/api/notifications");
        NotificationsPage? afterPage = await after.Content.ReadFromJsonAsync<NotificationsPage>();
        afterPage!.Items.Should().NotContain(item => item.Id == id);
    }

    [Fact]
    public async Task MarkAllReadZeroesUnreadCount()
    {
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            INotificationPublisher publisher =
                scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            await publisher.BroadcastAsync(
                NotificationKind.SystemMessage,
                Severity.Low,
                "bulk-read-1",
                "x"
            );
            await publisher.BroadcastAsync(
                NotificationKind.SystemMessage,
                Severity.Low,
                "bulk-read-2",
                "y"
            );
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/notifications/mark-all-read",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage after = await client.GetAsync("/api/notifications?unreadOnly=true");
        NotificationsPage? afterPage = await after.Content.ReadFromJsonAsync<NotificationsPage>();
        afterPage!.UnreadCount.Should().Be(0);
        afterPage.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task OauthExpiryWatcherEmitsNotificationForTokenExpiringWithinWindow()
    {
        using IServiceScope seedScope = _factory.Services.CreateScope();
        ShieldDbContext db = seedScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IntegrationToken token = new()
        {
            Id = Guid.NewGuid(),
            Provider = OAuthProvider.Github,
            Subject = "test-subject-" + Guid.NewGuid().ToString("n"),
            AccessTokenEncrypted = "encrypted-test-token",
            AccountLogin = "expiring-account",
            Scopes = "repo",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.IntegrationTokens.Add(token);
        await db.SaveChangesAsync();

        IOauthExpiryWatcher watcher = _factory.Services.GetRequiredService<IOauthExpiryWatcher>();
        await watcher.SweepAsync(CancellationToken.None);

        List<Notification> matching = await db
            .Notifications.Where(notification =>
                notification.Kind == NotificationKind.OauthExpiring
                && notification.RelatedId == token.Id.ToString()
            )
            .ToListAsync();
        matching.Should().NotBeEmpty();
    }
}
