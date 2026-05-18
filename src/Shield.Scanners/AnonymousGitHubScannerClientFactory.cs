using Octokit;
using Shield.Core.Domain;

namespace Shield.Scanners;

// Fallback used when the host hasn't registered a token-aware factory (unit tests,
// developer-mode scans before OAuth connect). Inherits the rate-limit story only if the
// caller swaps in a configured Octokit client via DI — the anon path here has no token and
// no handler chain, so it'll hit the 60/hr unauthenticated bucket. Shield.Api replaces this
// registration with a real factory that wires the GitHubRateLimitHandler and OAuth token.
public sealed class AnonymousGitHubScannerClientFactory : IGitHubScannerClientFactory
{
    private readonly IGitHubClient _client;

    public AnonymousGitHubScannerClientFactory(IGitHubClient client)
    {
        _client = client;
    }

    public Task<IGitHubClient> CreateForSourceAsync(Source source, CancellationToken ct) =>
        Task.FromResult(_client);
}
