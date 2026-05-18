namespace Shield.Core.Abstractions;

public abstract class RateLimitedException : Exception
{
    public DateTimeOffset RetryAt { get; init; }

    protected RateLimitedException(DateTimeOffset retryAt, string message)
        : base(message)
    {
        RetryAt = retryAt;
    }
}
