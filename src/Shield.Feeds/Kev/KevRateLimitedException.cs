using Shield.Core.Abstractions;

namespace Shield.Feeds.Kev;

public sealed class KevRateLimitedException : RateLimitedException
{
    public KevRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"KEV rate limit hit; retry at {retryAt:u}.") { }
}
