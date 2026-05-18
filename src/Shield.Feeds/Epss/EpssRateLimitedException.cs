using Shield.Core.Abstractions;

namespace Shield.Feeds.Epss;

public sealed class EpssRateLimitedException : RateLimitedException
{
    public EpssRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"EPSS rate limit hit; retry at {retryAt:u}.") { }
}
