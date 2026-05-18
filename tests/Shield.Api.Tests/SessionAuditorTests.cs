using FluentAssertions;
using NSubstitute;
using Shield.Api.Services.Auth;
using Shield.Api.Services.Security;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

public sealed class SessionAuditorTests
{
    private static ShieldUser MakeUser(string name = "testuser") =>
        new() { Id = Guid.NewGuid(), UserName = name };

    private static UserSession MakeSession(
        Guid userId,
        string? userAgent = "Mozilla/5.0",
        string? ip = "1.2.3.4"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserAgent = userAgent,
            RemoteIp = ip,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
        };

    private static (
        ISessionAuditor auditor,
        IAuditLogger audit,
        ISessionTracker tracker,
        INotificationPublisher notifications,
        ISecurityEventLogger securityLog
    ) BuildSut(bool sameDeviceRecently = false)
    {
        IAuditLogger audit = Substitute.For<IAuditLogger>();
        ISessionTracker tracker = Substitute.For<ISessionTracker>();
        INotificationPublisher notifications = Substitute.For<INotificationPublisher>();
        ISecurityEventLogger securityLog = Substitute.For<ISecurityEventLogger>();

        tracker
            .HasRecentSameDeviceSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(sameDeviceRecently));

        SessionAuditor auditor = new(audit, tracker, notifications, securityLog);
        return (auditor, audit, tracker, notifications, securityLog);
    }

    [Theory]
    [InlineData(SigninMethod.Password, "password", "password")]
    [InlineData(SigninMethod.GithubOAuth, "GitHub", "oauth.github")]
    [InlineData(SigninMethod.GoogleOAuth, "Google", "oauth.google")]
    [InlineData(SigninMethod.SlackOAuth, "Slack", "oauth.slack")]
    [InlineData(SigninMethod.InviteAcceptance, "invite", "invite")]
    [InlineData(SigninMethod.RecoveryCode, "recovery code", "recovery")]
    public void SigninMethodProducesCorrectLabels(
        SigninMethod method,
        string expectedHuman,
        string expectedAuditKey
    )
    {
        method.ToHumanLabel().Should().Be(expectedHuman);
        method.ToAuditKey().Should().Be(expectedAuditKey);
    }

    [Fact]
    public async Task RecordSigninAsyncWritesAuditWithCorrectMethodKey()
    {
        (ISessionAuditor auditor, IAuditLogger audit, _, _, _) = BuildSut();

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id);

        await auditor.RecordSigninAsync(user, session, SigninMethod.GithubOAuth);

        await audit
            .Received(1)
            .RecordAsync(
                "auth.session.create",
                "UserSession",
                session.Id.ToString(),
                Arg.Is<object>(detail => detail.ToString()!.Contains("oauth.github")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RecordSigninAsyncEmitsSecurityEventForEverySignin()
    {
        (ISessionAuditor auditor, _, _, _, ISecurityEventLogger securityLog) = BuildSut(
            sameDeviceRecently: true
        );

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id);

        await auditor.RecordSigninAsync(user, session, SigninMethod.Password);

        await securityLog
            .Received(1)
            .LogAsync(
                source: "shield.auth",
                eventType: "signin.password",
                severity: Severity.Low,
                remoteIp: session.RemoteIp,
                userAgent: session.UserAgent,
                userName: user.UserName,
                ct: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SameDeviceDedupSuppressesNotificationButStillEmitsSecurityEvent()
    {
        (
            ISessionAuditor auditor,
            _,
            _,
            INotificationPublisher notifications,
            ISecurityEventLogger securityLog
        ) = BuildSut(sameDeviceRecently: true);

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id);

        await auditor.RecordSigninAsync(user, session, SigninMethod.Password);

        await notifications
            .DidNotReceive()
            .PublishAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        await securityLog
            .Received(1)
            .LogAsync(
                source: Arg.Any<string>(),
                eventType: Arg.Any<string>(),
                severity: Arg.Any<Severity>(),
                remoteIp: Arg.Any<string?>(),
                userAgent: Arg.Any<string?>(),
                userName: Arg.Any<string?>(),
                path: Arg.Any<string?>(),
                host: Arg.Any<string?>(),
                jail: Arg.Any<string?>(),
                detailsJson: Arg.Any<string?>(),
                ct: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NewDeviceSendsNotificationWithHumanLabelInBody()
    {
        (ISessionAuditor auditor, _, _, INotificationPublisher notifications, _) = BuildSut(
            sameDeviceRecently: false
        );

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id, ip: "10.0.0.1");

        await auditor.RecordSigninAsync(user, session, SigninMethod.GithubOAuth);

        await notifications
            .Received(1)
            .PublishAsync(
                Arg.Is<Notification>(notif =>
                    notif.UserId == user.Id
                    && notif.Body.Contains("GitHub")
                    && notif.Body.Contains("10.0.0.1")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RecoveryCodeSigninEmitsHighSeveritySecurityEvent()
    {
        (ISessionAuditor auditor, _, _, _, ISecurityEventLogger securityLog) = BuildSut();

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id);

        await auditor.RecordSigninAsync(user, session, SigninMethod.RecoveryCode);

        await securityLog
            .Received(1)
            .LogAsync(
                source: "shield.auth",
                eventType: "signin.recovery",
                severity: Severity.High,
                remoteIp: Arg.Any<string?>(),
                userAgent: Arg.Any<string?>(),
                userName: Arg.Any<string?>(),
                ct: Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(SigninMethod.Password)]
    [InlineData(SigninMethod.GithubOAuth)]
    [InlineData(SigninMethod.GoogleOAuth)]
    [InlineData(SigninMethod.SlackOAuth)]
    [InlineData(SigninMethod.InviteAcceptance)]
    public async Task NonRecoverySigninsEmitLowSeveritySecurityEvent(SigninMethod method)
    {
        (ISessionAuditor auditor, _, _, _, ISecurityEventLogger securityLog) = BuildSut();

        ShieldUser user = MakeUser();
        UserSession session = MakeSession(user.Id);

        await auditor.RecordSigninAsync(user, session, method);

        await securityLog
            .Received(1)
            .LogAsync(
                source: "shield.auth",
                eventType: Arg.Any<string>(),
                severity: Severity.Low,
                remoteIp: Arg.Any<string?>(),
                userAgent: Arg.Any<string?>(),
                userName: Arg.Any<string?>(),
                ct: Arg.Any<CancellationToken>()
            );
    }
}
