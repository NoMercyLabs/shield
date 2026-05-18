using System.Net;
using Polly;
using Polly.Retry;

namespace Shield.Feeds.NpmRegistry;

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
                        .HandleResult(response => IsTransient(response.StatusCode)),
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

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500
        || status == HttpStatusCode.RequestTimeout
        || status == HttpStatusCode.TooManyRequests;
}
