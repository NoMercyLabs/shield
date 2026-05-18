using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record FeedStatusResponse(
    Feed Feed,
    DateTime? LastSuccessAt,
    string? LastError,
    DateTime NextRunAt,
    string? Cursor,
    bool Registered,
    int AdvisoryCount,
    DateTimeOffset? RateLimitResetAt = null
);
