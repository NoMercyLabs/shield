using System.Net.Mail;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shield.Channels.Smtp;
using Shield.Core.Domain;
using Shield.Core.Results;
using Xunit;

namespace Shield.Alerter.Tests;

public class SmtpChannelTests
{
    private static Finding NewFinding(Severity severity) =>
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
            Notes = $"finding-{severity}",
        };

    private static AlertChannel NewChannel(string password = "supersecret") =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Smtp,
            Name = "smtp",
            ConfigJsonEncrypted = JsonSerializer.Serialize(
                new
                {
                    host = "smtp.example.com",
                    port = 587,
                    useStartTls = true,
                    username = "shield",
                    password,
                    from = "shield@example.com",
                    to = new[] { "ops@example.com", "alerts@example.com" },
                    fromName = "Shield Alerts",
                }
            ),
            MinSeverity = Severity.Low,
            Enabled = true,
        };

    [Fact]
    public async Task SingleFindingProducesHtmlMessageWithCorrectSubject()
    {
        ISmtpSender sender = Substitute.For<ISmtpSender>();
        SmtpConfig? capturedConfig = null;
        MailMessage? capturedMessage = null;
        sender
            .SendAsync(
                Arg.Do<SmtpConfig>(cfg => capturedConfig = cfg),
                Arg.Do<MailMessage>(msg => capturedMessage = msg),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        SmtpChannel channel = new(sender, NullLogger<SmtpChannel>.Instance);
        Finding finding = NewFinding(Severity.Critical);

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            new[] { finding },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        capturedConfig!.Host.Should().Be("smtp.example.com");
        capturedMessage!.Subject.Should().Contain("Critical");
        capturedMessage.Subject.Should().Contain(finding.DedupKey);
        capturedMessage.IsBodyHtml.Should().BeTrue();
        capturedMessage.Body.Should().Contain("Critical");
        capturedMessage.To.Count.Should().Be(2);
        capturedMessage.From!.Address.Should().Be("shield@example.com");
        capturedMessage.From.DisplayName.Should().Be("Shield Alerts");
    }

    [Fact]
    public async Task FailureMessageNeverContainsPassword()
    {
        ISmtpSender sender = Substitute.For<ISmtpSender>();
        sender
            .SendAsync(Arg.Any<SmtpConfig>(), Arg.Any<MailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new SmtpException("auth failed for supersecret on host"));

        SmtpChannel channel = new(sender, NullLogger<SmtpChannel>.Instance);

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            new[] { NewFinding(Severity.High) },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Should().NotContain("supersecret");
        result.Error.Should().Contain("***");
    }

    [Fact]
    public void SmtpConfigValidatesPortAndRecipients()
    {
        new SmtpConfig("smtp.example.com", 587, true, "u", "p", "from@x", new[] { "to@x" })
            .IsValid()
            .Should()
            .BeTrue();
        new SmtpConfig("", 587, true, null, null, "from@x", new[] { "to@x" })
            .IsValid()
            .Should()
            .BeFalse();
        new SmtpConfig("smtp.example.com", 0, true, null, null, "from@x", new[] { "to@x" })
            .IsValid()
            .Should()
            .BeFalse();
        new SmtpConfig("smtp.example.com", 587, true, null, null, "from@x", Array.Empty<string>())
            .IsValid()
            .Should()
            .BeFalse();
    }
}
