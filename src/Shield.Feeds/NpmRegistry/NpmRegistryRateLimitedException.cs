using Shield.Core.Abstractions;

namespace Shield.Feeds.NpmRegistry;

public sealed class NpmRegistryRateLimitedException : RateLimitedException
{
    public NpmRegistryRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"npm registry rate limit hit; retry at {retryAt:u}.") { }
}
