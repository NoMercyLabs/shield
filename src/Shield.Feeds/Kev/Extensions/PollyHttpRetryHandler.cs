using System.Net;
using Polly;
using Polly.Extensions.Http;

namespace Shield.Feeds.Kev.Extensions;

public sealed class PollyHttpRetryHandler : DelegatingHandler
{
    private readonly IAsyncPolicy<HttpResponseMessage> _policy;

    public PollyHttpRetryHandler(IAsyncPolicy<HttpResponseMessage>? policy = null)
    {
        _policy = policy ?? BuildDefault();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => _policy.ExecuteAsync(ct => base.SendAsync(request, ct), cancellationToken);

    public static IAsyncPolicy<HttpResponseMessage> BuildDefault() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1))
            );
}
