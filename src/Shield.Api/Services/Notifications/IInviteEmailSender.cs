using Shield.Core.Domain;

namespace Shield.Api.Services.Notifications;

// Resolves an enabled SMTP AlertChannel and sends an invite email through its configuration.
// When no SMTP channel exists or the send fails, the implementation logs (and audit logs at
// the call site captures the outcome). The endpoint always returns 2xx if the invite row
// landed — operators can copy the accept URL out-of-band even if SMTP isn't wired yet.
public interface IInviteEmailSender
{
    Task<InviteEmailResult> SendAsync(
        Invite invite,
        string acceptUrl,
        string inviterLogin,
        IReadOnlyList<string> sourceGroupNames,
        CancellationToken ct
    );
}

public sealed record InviteEmailResult(bool Sent, string? Reason);
