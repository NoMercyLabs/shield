using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shield.Channels.Inbox;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Api.Tests;

// Tests for InboxChannel fanout behaviour: each finding (or digest) produces one
// INotificationPublisher.PublishAsync call per admin user.
public sealed class InboxChannelFanoutTests
{
    private static Finding NewFinding(Severity severity = Severity.High, string? notes = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            SourceId = 1,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = severity,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = Guid.NewGuid().ToString("N"),
            Notes = notes,
        };

    private static AlertChannel NewChannel() =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Inbox,
            Name = "inbox",
            ConfigJsonEncrypted = "{}",
            MinSeverity = Severity.Low,
            Enabled = true,
        };

    private static IAdminAudienceProvider AdminsWith(params Guid[] ids)
    {
        IAdminAudienceProvider provider = Substitute.For<IAdminAudienceProvider>();
        provider
            .GetAdminUserIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>(ids.ToList()));
        return provider;
    }

    [Fact]
    public async Task Three_findings_two_admins_produces_six_notifications()
    {
        Guid admin1 = Guid.NewGuid();
        Guid admin2 = Guid.NewGuid();

        IInboxStore store = Substitute.For<IInboxStore>();
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();
        List<Notification> published = [];
        publisher
            .PublishAsync(Arg.Do<Notification>(published.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        InboxChannel channel = new(
            store,
            AdminsWith(admin1, admin2),
            publisher,
            NullLogger<InboxChannel>.Instance
        );

        Finding[] findings = [NewFinding(), NewFinding(), NewFinding()];

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            findings,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        // 3 findings × 2 admins = 6 notifications
        published.Should().HaveCount(6);
        published.Select(notif => notif.UserId).Should().Contain(admin1).And.Contain(admin2);
    }

    [Fact]
    public async Task Digest_threshold_two_admins_produces_two_digest_notifications()
    {
        Guid admin1 = Guid.NewGuid();
        Guid admin2 = Guid.NewGuid();

        IInboxStore store = Substitute.For<IInboxStore>();
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();
        List<Notification> published = [];
        publisher
            .PublishAsync(Arg.Do<Notification>(published.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        InboxChannel channel = new(
            store,
            AdminsWith(admin1, admin2),
            publisher,
            NullLogger<InboxChannel>.Instance
        );

        // 5+ findings triggers digest mode
        Finding[] findings =
        [
            NewFinding(Severity.Low),
            NewFinding(Severity.Low),
            NewFinding(Severity.Medium),
            NewFinding(Severity.High),
            NewFinding(Severity.Critical),
        ];

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            findings,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        // 1 digest × 2 admins = 2 notifications
        published.Should().HaveCount(2);
        published.Should().AllSatisfy(notif => notif.Title.Should().Contain("5 findings"));
    }

    [Fact]
    public async Task Single_finding_notification_carries_finding_id_as_related()
    {
        Guid adminId = Guid.NewGuid();

        IInboxStore store = Substitute.For<IInboxStore>();
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();
        List<Notification> published = [];
        publisher
            .PublishAsync(Arg.Do<Notification>(published.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        InboxChannel channel = new(
            store,
            AdminsWith(adminId),
            publisher,
            NullLogger<InboxChannel>.Instance
        );

        Finding finding = NewFinding(Severity.Critical);
        await channel.SendAsync(NewChannel(), [finding], CancellationToken.None);

        published.Should().HaveCount(1);
        published[0].RelatedId.Should().Be(finding.Id.ToString());
        published[0].Severity.Should().Be(Severity.Critical);
    }

    [Fact]
    public async Task Zero_admins_produces_no_notifications_but_still_writes_inbox()
    {
        IInboxStore store = Substitute.For<IInboxStore>();
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();

        InboxChannel channel = new(
            store,
            AdminsWith(),
            publisher,
            NullLogger<InboxChannel>.Instance
        );

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [NewFinding()],
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        await publisher
            .DidNotReceive()
            .PublishAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        await store.Received(1).AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }
}
