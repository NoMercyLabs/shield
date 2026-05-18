using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shield.Channels.Inbox;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Alerter.Tests;

public class InboxChannelTests
{
    private static Finding NewFinding(Severity severity, string? notes = null) =>
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

    private static IAdminAudienceProvider TwoAdmins()
    {
        IAdminAudienceProvider provider = Substitute.For<IAdminAudienceProvider>();
        provider
            .GetAdminUserIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([Guid.NewGuid(), Guid.NewGuid()]));
        return provider;
    }

    [Fact]
    public async Task SingleFindingWritesInboxRowWithMatchingSeverityAndTitle()
    {
        IInboxStore store = Substitute.For<IInboxStore>();
        List<InboxMessage> captured = [];
        store
            .AddAsync(Arg.Do<InboxMessage>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();

        InboxChannel channel = new(
            store,
            TwoAdmins(),
            publisher,
            NullLogger<InboxChannel>.Instance
        );
        Finding finding = NewFinding(Severity.Critical, notes: "openssl 3.0.11 affected");

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [finding],
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        captured.Should().HaveCount(1);
        captured[0].Severity.Should().Be(Severity.Critical);
        captured[0].Title.Should().Contain("Critical");
        captured[0].Body.Should().Be("openssl 3.0.11 affected");
        captured[0].FindingId.Should().Be(finding.Id);
    }

    [Fact]
    public async Task DigestWritesSingleInboxRowAtMaxSeverity()
    {
        IInboxStore store = Substitute.For<IInboxStore>();
        List<InboxMessage> captured = [];
        store
            .AddAsync(Arg.Do<InboxMessage>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();

        InboxChannel channel = new(
            store,
            TwoAdmins(),
            publisher,
            NullLogger<InboxChannel>.Instance
        );
        Finding[] findings =
        [
            NewFinding(Severity.Low),
            NewFinding(Severity.Low),
            NewFinding(Severity.Medium),
            NewFinding(Severity.High),
            NewFinding(Severity.Critical),
            NewFinding(Severity.Low),
        ];

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            findings,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        captured.Should().HaveCount(1);
        captured[0].Severity.Should().Be(Severity.Critical);
        captured[0].Title.Should().Contain("6 findings");
        captured[0].FindingId.Should().BeNull();
    }

    [Fact]
    public async Task StoreExceptionMakesChannelReturnFailure()
    {
        IInboxStore store = Substitute.For<IInboxStore>();
        store
            .AddAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("db down"));
        INotificationPublisher publisher = Substitute.For<INotificationPublisher>();

        InboxChannel channel = new(
            store,
            TwoAdmins(),
            publisher,
            NullLogger<InboxChannel>.Instance
        );
        Finding finding = NewFinding(Severity.High);

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [finding],
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Be("db down");
    }
}
