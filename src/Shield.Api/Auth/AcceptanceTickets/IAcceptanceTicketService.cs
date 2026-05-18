namespace Shield.Api.Auth.AcceptanceTickets;

// Issued by the external-login pipeline when an external identity authenticated but Shield
// has no linked user. Consumed by /api/auth/accept-invite to bind the verified identity to
// a newly-created ShieldUser. The signed envelope is opaque to the SPA; the SPA only relays
// it back to the server.
public interface IAcceptanceTicketService
{
    string Issue(AcceptanceTicketPayload payload);
    bool TryValidate(string ticket, out AcceptanceTicketPayload? payload);
}
