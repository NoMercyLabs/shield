using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Api.Services.Auth;

// Centralises post-signin plumbing: audit record + sign-in notification + security event.
// Replaces the four bespoke RecordSessionCreatedAsync / audit.RecordAsync blocks that lived
// in AuthController, ExternalLoginController, OAuthController, and InviteAcceptanceController.
//
// Notification dedup: same-device (same UserAgent) within 24 h suppresses the notification
// only — the security event always fires so the Security view stays complete.
// Failures in any leg must NOT bubble to the caller — best-effort by design.
public sealed class SessionAuditor : ISessionAuditor
{
    private static readonly TimeSpan NotificationDedup = TimeSpan.FromHours(24);

    private readonly IAuditLogger _audit;
    private readonly ISessionTracker _sessionTracker;
    private readonly INotificationPublisher _notifications;
    private readonly ISecurityEventLogger _securityLog;

    public SessionAuditor(
        IAuditLogger audit,
        ISessionTracker sessionTracker,
        INotificationPublisher notifications,
        ISecurityEventLogger securityLog
    )
    {
        _audit = audit;
        _sessionTracker = sessionTracker;
        _notifications = notifications;
        _securityLog = securityLog;
    }

    public async Task RecordSigninAsync(
        ShieldUser user,
        UserSession session,
        SigninMethod method,
        CancellationToken ct = default
    )
    {
        string auditKey = method.ToAuditKey();
        string humanLabel = method.ToHumanLabel();

        try
        {
            await _audit.RecordAsync(
                "auth.session.create",
                "UserSession",
                session.Id.ToString(),
                new
                {
                    userId = user.Id,
                    method = auditKey,
                    userAgent = session.UserAgent,
                    remoteIp = session.RemoteIp,
                },
                ct
            );
        }
        catch
        {
            // Audit failures must not block sign-in.
        }

        Severity securitySeverity =
            method == SigninMethod.RecoveryCode ? Severity.High : Severity.Low;
        string eventType = $"signin.{auditKey}";

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: eventType,
                severity: securitySeverity,
                remoteIp: session.RemoteIp,
                userAgent: session.UserAgent,
                userName: user.UserName,
                ct: ct
            );
        }
        catch
        {
            // Security event failure must not block sign-in.
        }

        try
        {
            bool sameDeviceRecently = await _sessionTracker.HasRecentSameDeviceSessionAsync(
                user.Id,
                session.Id,
                session.UserAgent,
                NotificationDedup,
                ct
            );

            if (!sameDeviceRecently)
            {
                string where = session.RemoteIp ?? "unknown IP";
                await _notifications.PublishAsync(
                    new()
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        Kind = NotificationKind.SystemMessage,
                        Severity = Severity.Low,
                        Title = "New sign-in to your account",
                        Body = $"New {humanLabel} sign-in from {where} at {session.CreatedAt:u}.",
                        RelatedType = "UserSession",
                        RelatedId = session.Id.ToString(),
                        CreatedAt = DateTime.UtcNow,
                    },
                    ct
                );
            }
        }
        catch
        {
            // Notification failure must not block sign-in.
        }
    }
}
