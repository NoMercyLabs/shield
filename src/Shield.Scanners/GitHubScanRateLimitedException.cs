using Shield.Core.Abstractions;

namespace Shield.Scanners;

public sealed class GitHubScanRateLimitedException : RateLimitedException
{
    public GitHubScanRateLimitedException(DateTimeOffset retryAt, string? message = null)
        : base(retryAt, message ?? $"GitHub scan rate limit hit; retry at {retryAt:u}.") { }
}
