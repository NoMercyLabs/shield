using System.Net;
using Polly;
using Polly.Retry;

namespace Shield.Feeds.Ghsa;

public sealed class PollyTransientHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public PollyTransientHandler()
    {
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(
                new RetryStrategyOptions<HttpResponseMessage>
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .Handle<HttpRequestException>()
                        .HandleResult(response => IsTransient(response)),
                }
            )
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return await _pipeline
            .ExecuteAsync(
                async token => await base.SendAsync(request, token).ConfigureAwait(false),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // 403 from GitHub GraphQL means rate-limited (X-RateLimit-Remaining: 0) rather than an
    // auth failure when there is no API key configured. We only retry 403 when it is
    // genuinely a rate-limit response; a real auth failure (bad token) must NOT retry.
    public static bool IsTransient(HttpResponseMessage response)
    {
        if ((int)response.StatusCode >= 500)
            return true;
        if (response.StatusCode == HttpStatusCode.RequestTimeout)
            return true;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return true;
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return IsRateLimitedForbidden(response);
        return false;
    }

    public static bool IsRateLimitedForbidden(HttpResponseMessage response)
    {
        if (
            response.Headers.TryGetValues(
                "X-RateLimit-Remaining",
                out IEnumerable<string>? remaining
            )
        )
        {
            foreach (string value in remaining)
            {
                if (int.TryParse(value, out int count) && count == 0)
                    return true;
            }
        }
        return false;
    }
}
