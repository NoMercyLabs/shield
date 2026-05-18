using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;
using Xunit;

namespace Shield.Api.Tests;

// Covers IGithubAccessResolver in isolation: builds the user → GitHub-org → Shield-source
// mirror that AccessResolver layers on top of the manual SourceAccess rows. Uses the same
// fake "github" HttpClient pattern as CollaboratorsTests so /user/orgs responses are
// deterministic.
public sealed class GithubAccessResolverTests
{
    [Fact]
    public async Task User_without_github_login_returns_null()
    {
        using GithubAccessFactory factory = new();
        _ = factory.CreateClient();
        Guid userId = await SeedUserAsync(factory, withGithubLogin: false);

        IGithubAccessResolver resolver =
            factory.Services.GetRequiredService<IGithubAccessResolver>();
        GithubAccessSnapshot? snapshot = await resolver.GetAccessAsync(
            userId,
            CancellationToken.None
        );

        snapshot.Should().BeNull();
        factory.Handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task User_with_matching_org_gets_triage_on_orgs_sources()
    {
        using GithubAccessFactory factory = new();
        _ = factory.CreateClient();
        Guid userId = await SeedUserAsync(factory, withGithubLogin: true, githubSubject: "999");

        int sourceInOrg = await SeedSourceAsync(factory, "NoMercy-Entertainment", "media-server");
        int sourceOtherOrg = await SeedSourceAsync(factory, "SomeoneElse", "private-thing");

        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """
                [ { "login": "NoMercy-Entertainment" }, { "login": "NoMercyLabs" } ]
                """
            )
        );

        IGithubAccessResolver resolver =
            factory.Services.GetRequiredService<IGithubAccessResolver>();
        GithubAccessSnapshot? snapshot = await resolver.GetAccessAsync(
            userId,
            CancellationToken.None
        );

        snapshot.Should().NotBeNull();
        snapshot!.SourceAccess.Should().ContainKey(sourceInOrg);
        snapshot.SourceAccess[sourceInOrg].Level.Should().Be(SourceAccessLevel.Triage);
        snapshot.SourceAccess[sourceInOrg].Provenance.Should().Be("org:NoMercy-Entertainment");
        snapshot.SourceAccess.Should().NotContainKey(sourceOtherOrg);
        snapshot
            .OrgMemberships.Should()
            .BeEquivalentTo(new[] { "NoMercy-Entertainment", "NoMercyLabs" });

        factory.Handler.Requests.Should().HaveCount(1);
        factory.Handler.Requests[0].Url.Should().Contain("/user/orgs");
        // Per-user token must be used, NOT the admin's token.
        factory.Handler.Requests[0].Authorization.Should().Be("Bearer user-token-999");
    }

    [Fact]
    public async Task User_with_github_login_but_no_org_match_returns_empty_snapshot()
    {
        using GithubAccessFactory factory = new();
        _ = factory.CreateClient();
        Guid userId = await SeedUserAsync(factory, withGithubLogin: true, githubSubject: "999");

        int sourceOtherOrg = await SeedSourceAsync(factory, "SomeoneElse", "thing");

        factory.Handler.Responses.Add(
            new(HttpStatusCode.OK, """[ { "login": "Strangers" } ]""")
        );

        IGithubAccessResolver resolver =
            factory.Services.GetRequiredService<IGithubAccessResolver>();
        GithubAccessSnapshot? snapshot = await resolver.GetAccessAsync(
            userId,
            CancellationToken.None
        );

        snapshot.Should().NotBeNull();
        snapshot!.SourceAccess.Should().BeEmpty();
        snapshot.SourceAccess.Should().NotContainKey(sourceOtherOrg);
    }

    [Fact]
    public async Task Cache_hit_returns_same_result_without_second_http_call()
    {
        using GithubAccessFactory factory = new();
        _ = factory.CreateClient();
        Guid userId = await SeedUserAsync(factory, withGithubLogin: true, githubSubject: "999");
        await SeedSourceAsync(factory, "NoMercy-Entertainment", "media-server");

        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """[ { "login": "NoMercy-Entertainment" } ]"""
            )
        );

        IGithubAccessResolver resolver =
            factory.Services.GetRequiredService<IGithubAccessResolver>();
        GithubAccessSnapshot? first = await resolver.GetAccessAsync(userId, CancellationToken.None);
        GithubAccessSnapshot? second = await resolver.GetAccessAsync(
            userId,
            CancellationToken.None
        );

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.FetchedAt.Should().Be(first!.FetchedAt);
        factory.Handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Invalidate_drops_cached_entry_so_next_call_refetches()
    {
        using GithubAccessFactory factory = new();
        _ = factory.CreateClient();
        Guid userId = await SeedUserAsync(factory, withGithubLogin: true, githubSubject: "999");
        await SeedSourceAsync(factory, "NoMercy-Entertainment", "media-server");

        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """[ { "login": "NoMercy-Entertainment" } ]"""
            )
        );
        factory.Handler.Responses.Add(
            new(
                HttpStatusCode.OK,
                """[ { "login": "NoMercy-Entertainment" } ]"""
            )
        );

        IGithubAccessResolver resolver =
            factory.Services.GetRequiredService<IGithubAccessResolver>();
        _ = await resolver.GetAccessAsync(userId, CancellationToken.None);
        resolver.Invalidate(userId);
        _ = await resolver.GetAccessAsync(userId, CancellationToken.None);

        factory.Handler.Requests.Should().HaveCount(2);
    }

    // ---------- helpers ----------

    private static async Task<Guid> SeedUserAsync(
        GithubAccessFactory factory,
        bool withGithubLogin,
        string? githubSubject = null
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldUser user = new()
        {
            UserName = "test-user-" + Guid.NewGuid().ToString("n")[..6],
            Email = $"u-{Guid.NewGuid():n}@test",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };
        await userManager.CreateAsync(user, "Test1Pass!");

        if (withGithubLogin)
        {
            string subject = githubSubject ?? "999";
            await userManager.AddLoginAsync(
                user,
                new("Github", subject, user.UserName)
            );

            IOAuthTokenStore tokenStore =
                scope.ServiceProvider.GetRequiredService<IOAuthTokenStore>();
            await tokenStore.SaveSigninAsync(
                new(
                    OAuthProvider.Github,
                    AccessToken: "user-token-" + subject,
                    RefreshToken: null,
                    ExpiresAt: null,
                    Scopes: "read:user user:email read:org",
                    AccountLogin: user.UserName!,
                    AccountId: subject,
                    Extra: null
                ),
                subject,
                user.Id
            );
        }

        return user.Id;
    }

    private static async Task<int> SeedSourceAsync(
        GithubAccessFactory factory,
        string owner,
        string repo
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Source source = new()
        {
            Type = SourceType.GithubRepo,
            Name = $"{owner}/{repo}",
            ConfigJson = $"{{\"owner\":\"{owner}\",\"repo\":\"{repo}\"}}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private sealed class GithubAccessFactory : ShieldWebAppFactory
    {
        public GithubAccessFakeHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration(
                (_, config) =>
                {
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Shield:SingleUser"] = "false",
                            // Drop the cache TTL so the cache-hit test below stays fast — still long
                            // enough that the "second call hits cache" assertion holds within the test.
                            ["Shield:Access:GithubCacheMinutes"] = "15",
                        }
                    );
                }
            );
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => Handler);
            });
        }
    }

    public sealed record GithubAccessFakeResponse(HttpStatusCode Status, string Body);

    public sealed record GithubAccessCapturedRequest(string Url, string? Authorization);

    public sealed class GithubAccessFakeHandler : HttpMessageHandler
    {
        public List<GithubAccessFakeResponse> Responses { get; } = [];
        public List<GithubAccessCapturedRequest> Requests { get; } = [];

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

            GithubAccessFakeResponse spec = Responses[Requests.Count - 1];
            HttpResponseMessage response = new(spec.Status)
            {
                Content = new StringContent(spec.Body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
