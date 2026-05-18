namespace Shield.Api.Contracts;

public sealed record PushVapidKeyResponse(string PublicKey);

// Mirrors the W3C PushSubscription.toJSON() shape so the SPA can pipe the browser's
// subscription object straight through after a JSON serialise.
public sealed record PushSubscribeRequest(
    string Endpoint,
    PushSubscribeKeys Keys,
    string? UserAgent
);

public sealed record PushSubscribeKeys(string P256dh, string Auth);

public sealed record PushUnsubscribeRequest(string Endpoint);

public sealed record PushSubscriptionInfo(
    Guid Id,
    string Endpoint,
    string? UserAgent,
    DateTime CreatedAt,
    DateTime? LastDeliveredAt,
    bool IsCurrentDevice,
    string EndpointHash
);

public sealed record PushSubscriptionListResponse(
    IReadOnlyList<PushSubscriptionInfo> Subscriptions
);

public sealed record PushTestResponse(int Delivered);
