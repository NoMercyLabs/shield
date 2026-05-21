namespace Shield.Api.Services.Webhooks;

// Post-persistence dispatch contract for inbound webhook envelopes. Handlers run AFTER the
// envelope has been signature-verified and persisted; they decide whether the event is
// relevant to them and act accordingly. Each handler must be best-effort and never throw —
// the receiver runs them sequentially and a thrown handler would silence the rest.
//
// This is the seed of the receive-all-raw → project → handler pipeline. Today there is one
// handler (PR-closed rescan). New ones land alongside without touching the receiver.
public interface IInboundWebhookHandler
{
    Task HandleAsync(InboundWebhookContext context, CancellationToken ct);
}

public sealed record InboundWebhookContext(
    Shield.Core.Domain.OAuthProvider Provider,
    Guid EndpointId,
    string? EventType,
    byte[] Payload
);
