using Shield.Core.Abstractions;

namespace Shield.Feeds.Osv;

public sealed class OsvRateLimitedException : RateLimitedException
{
    public OsvRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"OSV rate limit hit; retry at {retryAt:u}.") { }
}
