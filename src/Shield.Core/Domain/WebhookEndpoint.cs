namespace Shield.Core.Domain;

// A registered inbound webhook destination — provider type + an encrypted shared secret that
// inbound deliveries are HMAC-verified against. The cleartext secret is shown to the operator
// exactly once at creation; storage is via IDataProtector("shield.webhooks"), same envelope as
// the OAuth token store, never an env var.
public sealed class WebhookEndpoint
{
    public Guid Id { get; set; }
    public OAuthProvider Provider { get; set; }
    public string Label { get; set; } = string.Empty;
    public string SecretEncrypted { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? LastDeliveryAt { get; set; }
    public string? LastDeliveryStatus { get; set; }
}
