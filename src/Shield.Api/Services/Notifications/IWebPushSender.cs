namespace Shield.Api.Services.Notifications;

// Single emit point for browser push delivery. The publisher pipeline calls Dispatch after
// the Notification row has been committed; this fan-outs the encrypted aes128gcm payload
// across every PushSubscription owned by the target user (or every admin on broadcast).
//
// Failures are absorbed: 410 Gone hard-deletes the subscription (browser cleared its grant),
// 5xx triggers one retry and then drops. Push reliability past best-effort would require a
// durable queue — v0.2 territory.
public interface IWebPushSender
{
    Task<string> EnsureVapidPublicKeyAsync(CancellationToken ct = default);
    Task DispatchAsync(PushPayload payload, Guid? userId, CancellationToken ct = default);
    Task DispatchToSubscriptionAsync(
        PushPayload payload,
        Guid subscriptionId,
        CancellationToken ct = default
    );
}

public sealed record PushPayload(
    string Title,
    string Body,
    string Severity,
    string? Url,
    string Tag
);
