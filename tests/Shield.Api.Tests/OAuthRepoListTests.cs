using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Api.Services.Auth;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Api.Tests;

// Covers GET /api/oauth/github/repos:
// - 400 when no IntegrationToken is connected (no GitHub OAuth row)
// - 200 with paged results when the token exists, following the Link: rel="next" header
// - Outgoing requests carry the Authorization: Bearer <token> + User-Agent + Accept headers
//
// The named "oauth" HttpClient is intercepted by a custom DelegatingHandler registered on the
// factory's IHttpClientFactory; no WireMock package needed.
public sealed class OAuthRepoListTests
{
    [Fact]
    public async Task ReposReturns400WhenGithubNotConnected()
    {
        using GitHubReposFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/oauth/github/repos");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReposFollowsLinkHeaderAndReturnsPaginatedResults()
    {
        using GitHubReposFactory factory = new();
        // Force host startup so the singleton handler we registered is reachable.
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "github-bearer-token",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user public_repo",
                AccountLogin: "octocat",
                AccountId: "1",
                Extra: null
            )
        );

        FakeGitHubHandler handler = factory.Handler;
        // Page 1 returns two repos and points at page 2 via Link: rel="next".
        handler.Pages.Add(
            new(
                Body: """
                [
                  {
                    "id": 1,
                    "name": "hello-world",
                    "full_name": "octocat/hello-world",
                    "description": "First repo",
                    "default_branch": "main",
                    "private": false,
                    "archived": false,
                    "fork": false,
                    "language": "C#"
                  },
                  {
                    "id": 2,
                    "name": "spoon-knife",
                    "full_name": "octocat/spoon-knife",
                    "description": null,
                    "default_branch": "main",
                    "private": true,
                    "archived": false,
                    "fork": true,
                    "language": null
                  }
                ]
                """,
                LinkHeader: "<https://api.github.com/user/repos?page=2&per_page=100>; rel=\"next\", <https://api.github.com/user/repos?page=2&per_page=100>; rel=\"last\""
            )
        );
        // Page 2 returns one repo and no Link header — pagination loop terminates.
        handler.Pages.Add(
            new(
                Body: """
                [
                  {
                    "id": 3,
                    "name": "private-prj",
                    "full_name": "noctocat/private-prj",
                    "description": "Org repo",
                    "default_branch": "master",
                    "private": true,
                    "archived": true,
                    "fork": false,
                    "language": "TypeScript"
                  }
                ]
                """,
                LinkHeader: null
            )
        );

        HttpResponseMessage response = await client.GetAsync("/api/oauth/github/repos");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        GitHubRepoListResponse? body =
            await response.Content.ReadFromJsonAsync<GitHubRepoListResponse>();
        body.Should().NotBeNull();
        body!.Total.Should().Be(3);
        body.Repos.Should().HaveCount(3);
        body.Repos[0].FullName.Should().Be("octocat/hello-world");
        body.Repos[0].Owner.Should().Be("octocat");
        body.Repos[0].DefaultBranch.Should().Be("main");
        body.Repos[0].Private.Should().BeFalse();
        body.Repos[1].Fork.Should().BeTrue();
        body.Repos[2].Owner.Should().Be("noctocat");
        body.Repos[2].Archived.Should().BeTrue();
        body.Repos[2].Language.Should().Be("TypeScript");

        // Two upstream pages consumed.
        handler.Requests.Should().HaveCount(2);

        // Every outgoing request carried the Bearer token + UA + Accept headers.
        foreach (CapturedRequest captured in handler.Requests)
        {
            captured.Authorization.Should().Be("Bearer github-bearer-token");
            captured.UserAgent.Should().StartWith("Shield/");
            captured.Accept.Should().Contain("application/vnd.github+json");
        }

        // First page request hits /user/repos with the default affiliation + per_page=100.
        handler.Requests[0].Url.Should().Contain("/user/repos");
        handler.Requests[0].Url.Should().Contain("per_page=100");
        handler.Requests[0].Url.Should().Contain("affiliation=");
        // Second page is the URL we returned in Link rel="next".
        handler.Requests[1].Url.Should().Contain("page=2");
    }

    [Fact]
    public async Task ReposResponseIsCachedPerUserForFiveMinutes()
    {
        using GitHubReposFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "github-bearer-token",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user",
                AccountLogin: "octocat",
                AccountId: "1",
                Extra: null
            )
        );

        FakeGitHubHandler handler = factory.Handler;
        handler.Pages.Add(
            new(
                Body: """
                [
                  {
                    "id": 1,
                    "name": "hello-world",
                    "full_name": "octocat/hello-world",
                    "default_branch": "main",
                    "private": false,
                    "archived": false,
                    "fork": false
                  }
                ]
                """,
                LinkHeader: null
            )
        );

        HttpResponseMessage first = await client.GetAsync("/api/oauth/github/repos");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage second = await client.GetAsync("/api/oauth/github/repos");
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second call must be served from cache — handler only saw one upstream call.
        handler.Requests.Should().HaveCount(1);
    }

    private sealed class GitHubReposFactory : ShieldWebAppFactory
    {
        public FakeGitHubHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                // Attach the fake handler to the named "oauth" + "github" HttpClients so both
                // legacy GitHubProvider paths (oauth) and the new rate-limit-aware repo listing
                // (github) send through it. PrimaryHandler swap is the standard ASP.NET Core
                // test seam — no DI rewire needed.
                services
                    .AddHttpClient("oauth")
                    .ConfigurePrimaryHttpMessageHandler(() => Handler);
                services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    public sealed record FakePage(string Body, string? LinkHeader);

    public sealed record CapturedRequest(
        string Url,
        string? Authorization,
        string? UserAgent,
        string? Accept
    );

    public sealed class FakeGitHubHandler : HttpMessageHandler
    {
        public List<FakePage> Pages { get; } = [];
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            string? auth = request.Headers.Authorization is { } authHeader
                ? $"{authHeader.Scheme} {authHeader.Parameter}"
                : null;
            string? ua =
                request.Headers.UserAgent.Count > 0 ? request.Headers.UserAgent.ToString() : null;
            string? accept =
                request.Headers.Accept.Count > 0 ? request.Headers.Accept.ToString() : null;
            Requests.Add(new(url, auth, ua, accept));

            if (Requests.Count > Pages.Count)
            {
                // Pagination loop should never overrun the queued pages; fail loud.
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("unexpected extra page request"),
                    }
                );
            }

            FakePage page = Pages[Requests.Count - 1];
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(page.Body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(page.LinkHeader))
                response.Headers.TryAddWithoutValidation("Link", page.LinkHeader);
            return Task.FromResult(response);
        }
    }
}
