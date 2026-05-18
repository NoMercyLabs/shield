namespace Shield.Api.Auth.AcceptanceTickets;

// Shape carried inside a short-lived signed ticket the device-flow / external-login pipeline
// issues to the SPA when an external identity authenticated but no Shield user exists yet
// (status = "needsInvite"). The /api/auth/accept-invite endpoint trusts a valid ticket as
// proof the external identity was just verified — no re-probe needed.
public sealed record AcceptanceTicketPayload(
    string Provider,
    string SubjectId,
    string Login,
    string? Email,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt
);
