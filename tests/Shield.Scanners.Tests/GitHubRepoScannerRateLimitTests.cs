using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Octokit;
using Shield.Core.Domain;
using Shield.Scanners;
using Xunit;

namespace Shield.Scanners.Tests;

public sealed class GitHubRepoScannerRateLimitTests
{
    [Fact]
    public async Task ScanAsync_throws_GitHubScanRateLimitedException_when_primary_limit_exhausted()
    {
        IGitHubClient client = Substitute.For<IGitHubClient>();
        IRepositoriesClient repoClient = Substitute.For<IRepositoriesClient>();
        client.Repository.Returns(repoClient);

        DateTimeOffset resetAt = DateTimeOffset.UtcNow.AddHours(1);
        long resetEpoch = resetAt.ToUnixTimeSeconds();

        Dictionary<string, string> headers = new()
        {
            ["X-RateLimit-Limit"] = "5000",
            ["X-RateLimit-Remaining"] = "0",
            ["X-RateLimit-Reset"] = resetEpoch.ToString(),
        };

        IResponse mockResponse = Substitute.For<IResponse>();
        mockResponse.StatusCode.Returns(HttpStatusCode.Forbidden);
        mockResponse.ContentType.Returns("application/json");
        mockResponse.Body.Returns("{\"message\":\"API rate limit exceeded\"}");
        mockResponse.Headers.Returns(headers);

        // Octokit's RateLimitExceededException constructor reads response.ApiInfo.RateLimit,
        // so supply a real ApiInfo backed by the same headers.
        ApiInfo apiInfo = new(
            links: new Dictionary<string, Uri>(),
            oauthScopes: [],
            acceptedOauthScopes: [],
            etag: null,
            rateLimit: new RateLimit(headers)
        );
        mockResponse.ApiInfo.Returns(apiInfo);

        RateLimitExceededException octokitEx = new(mockResponse);
        repoClient.Get(Arg.Any<string>(), Arg.Any<string>()).ThrowsAsync(octokitEx);

        GitHubRepoScanner scanner = new(
            new AnonymousGitHubScannerClientFactory(client),
            NewParserRegistry()
        );

        Source source = new()
        {
            Id = 42,
            Type = SourceType.GithubRepo,
            ConfigJson = JsonSerializer.Serialize(
                new
                {
                    owner = "acme",
                    repo = "widget",
                    branch = "main",
                }
            ),
        };

        Func<Task> act = async () => await scanner.ScanAsync(source, CancellationToken.None);

        GitHubScanRateLimitedException thrown = (
            await act.Should().ThrowAsync<GitHubScanRateLimitedException>()
        ).Which;

        thrown.RetryAt.ToUnixTimeSeconds().Should().Be(resetEpoch);
    }

    private static ParserRegistry NewParserRegistry() =>
        new(
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new(),
            new()
        );
}
