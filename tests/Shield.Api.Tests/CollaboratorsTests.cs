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

// Covers /api/collaborators/github/* — orgs listing, paginated members fan-out, and the
// token-revoked → 409 path.
//
// SingleUser=true means the test client is implicitly admin, so the Authorize policy passes
// without an explicit login step. The named "github" HttpClient gets its primary handler
// swapped for a deterministic fake — same pattern as OAuthRepoListTests.
public sealed class CollaboratorsTests
{
    [Fact]
    public async Task ListOrgsReturnsAdminTokenOrgs()
    {
        await using GithubCollabFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "admin-bearer",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user user:email repo read:org",
                AccountLogin: "stoneyeagle",
                AccountId: "1",
                Extra: null
            )
        );

        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """
                [
                  { "login": "NoMercy-Entertainment", "id": 100, "description": "NoMercy", "avatar_url": "https://avatars.githubusercontent.com/u/100" },
                  { "login": "NoMercyLabs", "id": 101, "description": null, "avatar_url": "https://avatars.githubusercontent.com/u/101" }
                ]
                """
            )
        );

        HttpResponseMessage response = await client.GetAsync("/api/collaborators/github/orgs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GithubOrgListResponse? body =
            await response.Content.ReadFromJsonAsync<GithubOrgListResponse>();
        body.Should().NotBeNull();
        body!.Orgs.Should().HaveCount(2);
        body.Orgs[0].Login.Should().Be("NoMercy-Entertainment");
        body.Orgs[1].Login.Should().Be("NoMercyLabs");

        factory.Handler.Requests.Should().HaveCount(1);
        factory.Handler.Requests[0].Url.Should().Contain("/user/orgs");
        factory.Handler.Requests[0].Authorization.Should().Be("Bearer admin-bearer");
    }

    [Fact]
    public async Task MembersPaginated()
    {
        await using GithubCollabFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "admin-bearer",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:org",
                AccountLogin: "stoneyeagle",
                AccountId: "1",
                Extra: null
            )
        );

        // Listing returns 2 members on page 1, plus Link rel="next" → hasMore=true.
        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """
                [
                  { "login": "stoneyeagle", "id": 1, "avatar_url": "https://avatars.githubusercontent.com/u/1" },
                  { "login": "fill84",       "id": 2, "avatar_url": "https://avatars.githubusercontent.com/u/2" }
                ]
                """,
                LinkHeader: "<https://api.github.com/orgs/NoMercy-Entertainment/members?per_page=2&page=2>; rel=\"next\""
            )
        );
        // Per-member /users/{login} enrichment — order matches the listing.
        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """
                { "login": "stoneyeagle", "id": 1, "name": "Patrick Staarink", "email": "stoneyeagle@example.test", "avatar_url": "https://avatars.githubusercontent.com/u/1" }
                """
            )
        );
        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """
                { "login": "fill84", "id": 2, "name": "Phil Pelzer", "email": null, "avatar_url": "https://avatars.githubusercontent.com/u/2" }
                """
            )
        );

        HttpResponseMessage response = await client.GetAsync(
            "/api/collaborators/github/orgs/NoMercy-Entertainment/members?perPage=2&page=1"
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        GithubMemberListResponse? body =
            await response.Content.ReadFromJsonAsync<GithubMemberListResponse>();
        body.Should().NotBeNull();
        body!.Members.Should().HaveCount(2);
        body.Members[0].Login.Should().Be("stoneyeagle");
        body.Members[0].Name.Should().Be("Patrick Staarink");
        body.Members[0].GithubId.Should().Be("1");
        body.Members[1].Login.Should().Be("fill84");
        body.Members[1].Name.Should().Be("Phil Pelzer");
        body.Page.Should().Be(1);
        body.PerPage.Should().Be(2);
        body.HasMore.Should().BeTrue();

        // 1 listing + 2 enrichment fan-outs = 3 upstream calls.
        factory.Handler.Requests.Should().HaveCount(3);
        factory.Handler.Requests[0].Url.Should().Contain("/orgs/NoMercy-Entertainment/members");
        factory.Handler.Requests[0].Url.Should().Contain("per_page=2");
        factory.Handler.Requests[0].Url.Should().Contain("page=1");
        factory.Handler.Requests[1].Url.Should().Contain("/users/stoneyeagle");
        factory.Handler.Requests[2].Url.Should().Contain("/users/fill84");
    }

    [Fact]
    public async Task TokenRevokedReturns409()
    {
        await using GithubCollabFactory factory = new();
        HttpClient client = factory.CreateClient();

        IOAuthTokenStore store = factory.Services.GetRequiredService<IOAuthTokenStore>();
        await store.SaveAsync(
            new(
                OAuthProvider.Github,
                AccessToken: "stale-bearer",
                RefreshToken: null,
                ExpiresAt: null,
                Scopes: "read:user user:email repo read:org",
                AccountLogin: "stoneyeagle",
                AccountId: "1",
                Extra: null
            )
        );

        factory.Handler.Responses.Add(
            new(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}")
        );

        HttpResponseMessage response = await client.GetAsync("/api/collaborators/github/orgs");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("github_token_invalid");
        body.Should().Contain("reconnect");
    }

    private sealed class GithubCollabFactory : ShieldWebAppFactory
    {
        public FakeGithubHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    public sealed record FakeResponse(
        HttpStatusCode Status,
        string Body,
        string? LinkHeader = null
    );

    public sealed record CapturedRequest(string Url, string? Authorization);

    public sealed class FakeGithubHandler : HttpMessageHandler
    {
        public List<FakeResponse> Responses { get; } = [];
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
            Requests.Add(new(url, auth));

            if (Requests.Count > Responses.Count)
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("unexpected extra upstream call"),
                    }
                );
            }

            FakeResponse spec = Responses[Requests.Count - 1];
            HttpResponseMessage response = new(spec.Status)
            {
                Content = new StringContent(spec.Body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(spec.LinkHeader))
                response.Headers.TryAddWithoutValidation("Link", spec.LinkHeader);
            return Task.FromResult(response);
        }
    }
}
