namespace Shield.Core.Domain;

// One row per browser-issued Web Push endpoint. Endpoint is the upstream FCM/Mozilla/
// Apple URL the browser hands the SPA — globally unique, used as the dedup key on insert.
// P256dh + Auth are the client-side ECDH keys that the WebPush library uses to encrypt
// the AES128GCM payload (RFC 8291). LastDeliveredAt is bumped only on a successful 2xx;
// 410 Gone responses hard-delete the row.
public sealed class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastDeliveredAt { get; set; }
}
