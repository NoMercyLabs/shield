namespace Shield.Core.Domain;

// Raw record of an inbound webhook delivery. Captured BEFORE projection so future detectors
// can backfill from history without losing event types the receiver doesn't know about yet.
// PayloadJson is stored verbatim; HeadersJson holds the small allow-list of headers we care
// about (event type, delivery id, signature header). SignatureValid is the verification
// outcome at receive time — once persisted it doesn't change.
public sealed class WebhookEnvelope
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public OAuthProvider Provider { get; set; }

    // Provider-supplied event type, e.g. "ping" / "push" / "pull_request" / "workflow_run".
    // Pulled from X-GitHub-Event / X-Gitea-Event / X-Gitlab-Event at receive time.
    public string? EventType { get; set; }

    // Provider-supplied unique delivery id, used to dedupe replayed deliveries.
    public string? DeliveryId { get; set; }

    public string HeadersJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = string.Empty;
    public bool SignatureValid { get; set; }

    // When SignatureValid is false, why — "missing-signature", "hmac-mismatch", "bad-prefix",
    // "secret-undecryptable". Used in the delivery history UI to debug failures.
    public string? Reason { get; set; }

    public DateTime ReceivedAt { get; set; }
}
