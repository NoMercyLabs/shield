using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Alerter.Tests;

public class AlertDispatcherTests
{
    private static Finding NewFinding(Severity severity, string? dedup = null) => new()
    {
        Id = Guid.NewGuid(),
        SourceId = 1,
        InventoryItemId = 1,
        AdvisoryRefId = Guid.NewGuid(),
        Severity = severity,
        FirstSeenAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
        State = FindingState.Open,
        DedupKey = dedup ?? Guid.NewGuid().ToString("N"),
    };

    private static AlertChannel NewChannel(
        ChannelType type,
        Severity minSeverity = Severity.Low,
        bool enabled = true
    ) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = type.ToString(),
        ConfigJsonEncrypted = "{}",
        MinSeverity = minSeverity,
        Enabled = enabled,
    };

    private static IAlertChannel StubChannel(
        ChannelType type,
        AlertResult? result = null,
        Action<IReadOnlyList<Finding>>? recordFindings = null
    )
    {
        IAlertChannel channel = Substitute.For<IAlertChannel>();
        channel.ChannelType.Returns(type);
        channel
            .SendAsync(Arg.Any<AlertChannel>(), Arg.Any<IReadOnlyList<Finding>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                IReadOnlyList<Finding> findings = callInfo.ArgAt<IReadOnlyList<Finding>>(1);
                recordFindings?.Invoke(findings);
                return ValueTask.FromResult(result ?? AlertResult.Ok(findings.Count));
            });
        return channel;
    }

    [Fact]
    public async Task FiltersByMinSeverity()
    {
        List<IReadOnlyList<Finding>> sent = new();
        IAlertChannel impl = StubChannel(ChannelType.Inbox, recordFindings: sent.Add);
        AlertDispatcher dispatcher = new(new[] { impl }, NullLogger<AlertDispatcher>.Instance);

        Finding low = NewFinding(Severity.Low);
        Finding high = NewFinding(Severity.High);
        AlertChannel channel = NewChannel(ChannelType.Inbox, Severity.High);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            new[] { low, high },
            new[] { channel },
            CancellationToken.None
        );

        sent.Should().HaveCount(1);
        sent[0].Should().ContainSingle().Which.Id.Should().Be(high.Id);
        events.Should().HaveCount(1);
        events[0].FindingId.Should().Be(high.Id);
        events[0].Status.Should().Be(AlertStatus.Sent);
    }

    [Fact]
    public async Task DigestsWhenFiveOrMoreFindings()
    {
        List<IReadOnlyList<Finding>> sent = new();
        IAlertChannel impl = StubChannel(ChannelType.Inbox, recordFindings: sent.Add);
        AlertDispatcher dispatcher = new(new[] { impl }, NullLogger<AlertDispatcher>.Instance);

        List<Finding> findings = Enumerable.Range(0, 6)
            .Select(_ => NewFinding(Severity.High))
            .ToList();
        AlertChannel channel = NewChannel(ChannelType.Inbox);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            findings,
            new[] { channel },
            CancellationToken.None
        );

        sent.Should().HaveCount(1);
        sent[0].Should().HaveCount(6);
        events.Should().HaveCount(6);
        events.Select(alertEvent => alertEvent.FindingId)
            .Should().BeEquivalentTo(findings.Select(finding => finding.Id));
    }

    [Fact]
    public async Task SendsOnePerFindingWhenBelowDigestThreshold()
    {
        List<IReadOnlyList<Finding>> sent = new();
        IAlertChannel impl = StubChannel(ChannelType.Inbox, recordFindings: sent.Add);
        AlertDispatcher dispatcher = new(new[] { impl }, NullLogger<AlertDispatcher>.Instance);

        List<Finding> findings = Enumerable.Range(0, 4)
            .Select(_ => NewFinding(Severity.High))
            .ToList();
        AlertChannel channel = NewChannel(ChannelType.Inbox);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            findings,
            new[] { channel },
            CancellationToken.None
        );

        sent.Should().HaveCount(4);
        sent.Should().OnlyContain(batch => batch.Count == 1);
        events.Should().HaveCount(4);
    }

    [Fact]
    public async Task FailedChannelLogsButDoesNotBreakOthers()
    {
        IAlertChannel failing = StubChannel(ChannelType.Discord, AlertResult.Fail("boom"));
        List<IReadOnlyList<Finding>> sent = new();
        IAlertChannel ok = StubChannel(ChannelType.Inbox, recordFindings: sent.Add);
        AlertDispatcher dispatcher = new(
            new[] { failing, ok },
            NullLogger<AlertDispatcher>.Instance
        );

        Finding finding = NewFinding(Severity.Critical);
        AlertChannel discord = NewChannel(ChannelType.Discord);
        AlertChannel inbox = NewChannel(ChannelType.Inbox);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            new[] { finding },
            new[] { discord, inbox },
            CancellationToken.None
        );

        sent.Should().HaveCount(1);
        events.Should().HaveCount(2);
        events.Single(alertEvent => alertEvent.ChannelId == discord.Id).Status
            .Should().Be(AlertStatus.Failed);
        events.Single(alertEvent => alertEvent.ChannelId == discord.Id).Error
            .Should().Be("boom");
        events.Single(alertEvent => alertEvent.ChannelId == inbox.Id).Status
            .Should().Be(AlertStatus.Sent);
    }

    [Fact]
    public async Task ChannelThatThrowsIsCaughtAndMarkedFailed()
    {
        IAlertChannel throwing = Substitute.For<IAlertChannel>();
        throwing.ChannelType.Returns(ChannelType.Discord);
        throwing
            .SendAsync(Arg.Any<AlertChannel>(), Arg.Any<IReadOnlyList<Finding>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<AlertResult>>(_ => throw new InvalidOperationException("kaboom"));

        AlertDispatcher dispatcher = new(
            new[] { throwing },
            NullLogger<AlertDispatcher>.Instance
        );

        Finding finding = NewFinding(Severity.Critical);
        AlertChannel discord = NewChannel(ChannelType.Discord);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            new[] { finding },
            new[] { discord },
            CancellationToken.None
        );

        events.Should().HaveCount(1);
        events[0].Status.Should().Be(AlertStatus.Failed);
        events[0].Error.Should().Be("kaboom");
    }

    [Fact]
    public async Task DisabledChannelsAreSkipped()
    {
        List<IReadOnlyList<Finding>> sent = new();
        IAlertChannel impl = StubChannel(ChannelType.Inbox, recordFindings: sent.Add);
        AlertDispatcher dispatcher = new(new[] { impl }, NullLogger<AlertDispatcher>.Instance);

        Finding finding = NewFinding(Severity.High);
        AlertChannel channel = NewChannel(ChannelType.Inbox, enabled: false);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            new[] { finding },
            new[] { channel },
            CancellationToken.None
        );

        sent.Should().BeEmpty();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingChannelImplementationMarksFindingsFailed()
    {
        AlertDispatcher dispatcher = new(
            Array.Empty<IAlertChannel>(),
            NullLogger<AlertDispatcher>.Instance
        );

        Finding finding = NewFinding(Severity.High);
        AlertChannel channel = NewChannel(ChannelType.Discord);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            new[] { finding },
            new[] { channel },
            CancellationToken.None
        );

        events.Should().HaveCount(1);
        events[0].Status.Should().Be(AlertStatus.Failed);
        events[0].Error.Should().Contain("Discord");
    }

    [Fact]
    public async Task EmitsAlertEventPerFindingChannelPair()
    {
        IAlertChannel discordImpl = StubChannel(ChannelType.Discord);
        IAlertChannel inboxImpl = StubChannel(ChannelType.Inbox);
        AlertDispatcher dispatcher = new(
            new[] { discordImpl, inboxImpl },
            NullLogger<AlertDispatcher>.Instance
        );

        List<Finding> findings = new() { NewFinding(Severity.High), NewFinding(Severity.High) };
        AlertChannel discord = NewChannel(ChannelType.Discord);
        AlertChannel inbox = NewChannel(ChannelType.Inbox);

        IReadOnlyList<AlertEvent> events = await dispatcher.DispatchAsync(
            findings,
            new[] { discord, inbox },
            CancellationToken.None
        );

        events.Should().HaveCount(4);
        events.Count(alertEvent => alertEvent.ChannelId == discord.Id).Should().Be(2);
        events.Count(alertEvent => alertEvent.ChannelId == inbox.Id).Should().Be(2);
    }
}
