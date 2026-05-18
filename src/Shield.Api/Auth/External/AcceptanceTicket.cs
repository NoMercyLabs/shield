// HANDSHAKE — invite-link signup agent ↔ external-login agent
// ----------------------------------------------------------------
// The invite-link agent consumes a short-lived signed "acceptance ticket" issued by THIS
// pipeline (the external-login / device-flow side) when an external identity authenticated
// but no Shield user exists yet. The /api/auth/external/{provider}/poll endpoint should
// return `{ status: "needsInvite", acceptanceTicket: "<token>", ... }` in that case.
//
// Ticket shape lives in Shield.Api.Auth.AcceptanceTickets:
//   - AcceptanceTicket.cs            (payload record: Provider, SubjectId, Login, Email, IssuedAt, ExpiresAt)
//   - IAcceptanceTicketService.cs    (Issue + TryValidate)
//   - AcceptanceTicketService.cs     (HMAC-SHA256 envelope over base64url JSON, 5-min TTL)
//
// To issue:
//   IAcceptanceTicketService tickets;  // already registered in Program.cs DI as singleton
//   string token = tickets.Issue(new AcceptanceTicketPayload(
//       provider: "github",
//       subjectId: signin.Subject,
//       login: signin.Login,
//       email: signin.Email,
//       issuedAt: DateTimeOffset.UtcNow,
//       expiresAt: AcceptanceTicketService.DefaultExpiry()  // = now + 5 minutes
//   ));
//
// Hand `token` to the SPA in the poll response. The SPA POSTs it to /api/auth/accept-invite
// alongside the invite token from the email link. The invite-link agent validates the ticket
// signature + TTL, then trusts the (provider, subjectId, login, email) fields without
// re-probing the upstream provider.
//
// If you'd rather not trust signed tickets, the alternative is to keep server-side state
// (e.g. an EphemeralExternalIdentity table) and hand a ticket id to the SPA. The invite-link
// agent picked the signed-envelope path because: (1) no extra table, (2) no cleanup worker
// for expired entries, (3) the JWT signing key already exists in config.
