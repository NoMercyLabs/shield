using Shield.Core.Domain;

namespace Shield.Api.Contracts;

// Returned by /api/oauth/{provider}/start — UI redirects the browser here.
public sealed record OAuthStartResponse(string AuthorizationUrl, string State);

// Status flips connected=true once the callback persists a token row.
public sealed record OAuthStatusResponse(
    OAuthProvider Provider,
    bool Connected,
    string? AccountLogin,
    string? AccountId,
    string? Scopes,
    DateTime? ExpiresAt,
    DateTime? UpdatedAt
);

public sealed record SlackChannelInfo(string Id, string Name, bool IsPrivate);

public sealed record SlackChannelsResponse(IReadOnlyList<SlackChannelInfo> Channels);
