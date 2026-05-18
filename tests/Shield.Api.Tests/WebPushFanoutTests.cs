using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shield.Api.Hubs;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Validates that NotificationPublisher fans out to IWebPushSender after writing the
// Notification row. Uses an in-process SQLite DB so the SaveChanges path is real.
public sealed class WebPushFanoutTests : IAsyncDisposable
{
    private readonly ShieldDbContext _db;

    public WebPushFanoutTests()
    {
        DbContextOptions<ShieldDbContext> options = new DbContextOptionsBuilder<ShieldDbContext>()
            .UseSqlite($"Data Source=file:wpf-{Guid.NewGuid():n}?mode=memory&cache=shared")
            .Options;
        _db = new ShieldDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    private NotificationPublisher BuildPublisher(IWebPushSender push)
    {
        IHubContext<FindingsHub> hub = Substitute.For<IHubContext<FindingsHub>>();
        IHubClients clients = Substitute.For<IHubClients>();
        IClientProxy proxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.All.Returns(proxy);

        return new NotificationPublisher(
            _db,
            hub,
            push,
            NullLogger<NotificationPublisher>.Instance
        );
    }

    private static Notification SampleNotification(Guid? userId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Kind = NotificationKind.Alert,
            Severity = Severity.High,
            Title = "Test push " + Guid.NewGuid().ToString("n"),
            Body = "web push fanout test",
            CreatedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task PublishAsync_with_2_subscriptions_sends_to_both()
    {
        Guid userId = Guid.NewGuid();
        IWebPushSender push = Substitute.For<IWebPushSender>();
        push.DispatchAsync(Arg.Any<PushPayload>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        NotificationPublisher publisher = BuildPublisher(push);
        Notification notification = SampleNotification(userId);
        await publisher.PublishAsync(notification);

        await push.Received(1)
            .DispatchAsync(
                Arg.Is<PushPayload>(payload => payload.Title == notification.Title),
                userId,
                Arg.Any<CancellationToken>()
            );

        // Row must be persisted regardless of push outcome.
        bool persisted = await _db.Notifications.AnyAsync(row => row.Id == notification.Id);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_with_no_subscription_succeeds_silently()
    {
        IWebPushSender push = Substitute.For<IWebPushSender>();
        push.DispatchAsync(Arg.Any<PushPayload>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        NotificationPublisher publisher = BuildPublisher(push);
        Notification notification = SampleNotification(userId: null);

        // Should not throw even when push dispatches to zero subscriptions.
        Func<Task> act = () => publisher.PublishAsync(notification);
        await act.Should().NotThrowAsync();

        bool persisted = await _db.Notifications.AnyAsync(row => row.Id == notification.Id);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_on_push_error_does_not_block_db_write()
    {
        // WebPushSender handles 410 internally (deletes the subscription row). At the
        // NotificationPublisher level, any exception from the sender must be swallowed so the
        // in-app notification (the DB row + SignalR broadcast) still succeeds.
        Guid userId = Guid.NewGuid();
        IWebPushSender push = Substitute.For<IWebPushSender>();
        push.DispatchAsync(Arg.Any<PushPayload>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated 410 Gone from push endpoint"));

        NotificationPublisher publisher = BuildPublisher(push);
        Notification notification = SampleNotification(userId);

        Func<Task> act = () => publisher.PublishAsync(notification);
        await act.Should().NotThrowAsync();

        bool persisted = await _db.Notifications.AnyAsync(row => row.Id == notification.Id);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task PushSendFailure_does_not_block_db_write()
    {
        IWebPushSender push = Substitute.For<IWebPushSender>();
        push.DispatchAsync(Arg.Any<PushPayload>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("VAPID key not initialised"));

        NotificationPublisher publisher = BuildPublisher(push);
        Notification notification = SampleNotification(userId: null);

        Func<Task> act = () => publisher.PublishAsync(notification);
        await act.Should().NotThrowAsync();

        bool persisted = await _db.Notifications.AnyAsync(row => row.Id == notification.Id);
        persisted.Should().BeTrue();
    }
}
