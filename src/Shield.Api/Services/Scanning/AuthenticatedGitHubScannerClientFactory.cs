using System.Text.Json;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Scanners;

namespace Shield.Api.Services.Scanning;

// Production factory: pulls the OAuth GitHub token, falls back to a per-source PAT inside
// ConfigJson, and finally surrenders to an unauthenticated client. Every produced client
// runs through the GitHubRateLimitHandler so the bucket is honoured per principal.
public sealed class AuthenticatedGitHubScannerClientFactory : IGitHubScannerClientFactory
{
    private readonly IGitHubClientFactory _githubClientFactory;
    private readonly IOAuthTokenAccessor _tokenAccessor;
    private readonly IGitHubClient _anonymousClient;

    public AuthenticatedGitHubScannerClientFactory(
        IGitHubClientFactory githubClientFactory,
        IOAuthTokenAccessor tokenAccessor,
        IGitHubClient anonymousClient
    )
    {
        _githubClientFactory = githubClientFactory;
        _tokenAccessor = tokenAccessor;
        _anonymousClient = anonymousClient;
    }

    public async Task<IGitHubClient> CreateForSourceAsync(Source source, CancellationToken ct)
    {
        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token))
        {
            // Inline per-source PAT — legacy escape hatch carried forward from v0 configs.
            try
            {
                using JsonDocument doc = JsonDocument.Parse(source.ConfigJson);
                if (
                    doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("token", out JsonElement tokenEl)
                    && tokenEl.ValueKind == JsonValueKind.String
                )
                    token = tokenEl.GetString();
            }
            catch (JsonException)
            {
                // Malformed configs fall through to anon.
            }
        }

        return string.IsNullOrEmpty(token) ? _anonymousClient : _githubClientFactory.Create(token);
    }
}
