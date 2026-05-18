using Shield.Core.Abstractions;

namespace Shield.Feeds.Ghsa;

public sealed class GhsaRateLimitedException : RateLimitedException
{
    public GhsaRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"GHSA rate limit hit; retry at {retryAt:u}.") { }
}
