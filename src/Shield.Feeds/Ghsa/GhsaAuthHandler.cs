using System.Net.Http.Headers;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Ghsa;

// Resolves a bearer token from IGhsaAuthTokenSource on every outbound request — bound at
// the handler level (not the HttpClient defaults) so an OAuth token that rotates mid-run
// is picked up on the next sync without restarting the host. When the source returns
// null, the request goes out anonymously; GHSA returns public-advisory data either way,
// just at a 60/hr quota.
public sealed class GhsaAuthHandler : DelegatingHandler
{
    private readonly IGhsaAuthTokenSource _tokens;

    public GhsaAuthHandler(IGhsaAuthTokenSource tokens)
    {
        _tokens = tokens;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.Headers.Authorization is null)
        {
            string? token = await _tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
