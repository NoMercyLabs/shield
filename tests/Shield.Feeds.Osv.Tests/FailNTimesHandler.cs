using System.Net;

namespace Shield.Feeds.Osv.Tests;

internal sealed class FailNTimesHandler : DelegatingHandler
{
    private readonly HttpStatusCode _failureStatus;
    private readonly string _pathSuffix;
    private int _remainingFailures;

    public int FailureCalls { get; private set; }
    public int SuccessCalls { get; private set; }

    public FailNTimesHandler(int failures, HttpStatusCode failureStatus, string pathSuffix)
    {
        _remainingFailures = failures;
        _failureStatus = failureStatus;
        _pathSuffix = pathSuffix;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool matchesPath = request.RequestUri is not null
            && request.RequestUri.AbsolutePath.EndsWith(_pathSuffix, StringComparison.Ordinal);

        if (matchesPath && _remainingFailures > 0)
        {
            _remainingFailures--;
            FailureCalls++;
            return new(_failureStatus) { RequestMessage = request };
        }

        if (matchesPath) SuccessCalls++;
        return await base.SendAsync(request, cancellationToken);
    }
}
