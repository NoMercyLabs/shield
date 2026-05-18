using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class WebhooksTests : IClassFixture<WebhooksTests.Factory>
{
    private const string GithubSecret = "github-test-secret";
    private const string DependabotSecret = "dependabot-test-secret";

    private readonly Factory _factory;

    public WebhooksTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Github_webhook_rejects_request_with_invalid_signature()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);
        HttpClient client = _factory.CreateClient();

        byte[] payload = Encoding.UTF8.GetBytes("{}");
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/github");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('0', 64));
        request.Headers.Add("X-GitHub-Event", "pull_request");

        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Github_webhook_with_missing_signature_returns_401()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/webhooks/github",
            new StringContent("{}", Encoding.UTF8, "application/json")
        );
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Github_pull_request_with_valid_signature_invokes_pr_comment_service()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);

        IPrCommentService stub = Substitute.For<IPrCommentService>();
        stub.ProcessPullRequestAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new PrCommentResult(true, 1, 0, null, "no matches"));
        _factory.ReplacePrCommentService(stub);

        HttpClient client = _factory.CreateClient();
        string body = """
            {
              "action": "opened",
              "number": 42,
              "pull_request": {
                "number": 42,
                "head": {"ref": "feature/x", "sha": "abc123"},
                "base": {"ref": "master", "sha": "def456"}
              },
              "repository": {
                "id": 1,
                "name": "shield",
                "full_name": "NoMercyLabs/shield",
                "owner": {"login": "NoMercyLabs"}
              }
            }
            """;
        byte[] payload = Encoding.UTF8.GetBytes(body);

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/github");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Sign(payload, GithubSecret));
        request.Headers.Add("X-GitHub-Event", "pull_request");

        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await stub.Received(1)
            .ProcessPullRequestAsync(
                "NoMercyLabs",
                "shield",
                42,
                "feature/x",
                "master",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Github_non_pullrequest_event_is_ignored_but_signature_still_required()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);
        HttpClient client = _factory.CreateClient();

        byte[] payload = Encoding.UTF8.GetBytes("{}");
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/github");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Sign(payload, GithubSecret));
        request.Headers.Add("X-GitHub-Event", "push");

        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("ignored");
    }

    [Fact]
    public async Task Dependabot_alert_persists_advisory_row()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);
        HttpClient client = _factory.CreateClient();
        string body = """
            {
              "action": "created",
              "alert": {
                "number": 7,
                "state": "open",
                "dependency": {
                  "package": {"ecosystem": "npm", "name": "lodash"},
                  "manifest_path": "package.json",
                  "scope": "runtime"
                },
                "security_advisory": {
                  "ghsa_id": "GHSA-test-1234-abcd",
                  "cve_id": "CVE-2024-9999",
                  "summary": "Prototype pollution",
                  "description": "Test description",
                  "severity": "high",
                  "cvss": {"score": 7.5, "vector_string": "CVSS"},
                  "published_at": "2024-01-01T00:00:00Z",
                  "updated_at": "2024-01-02T00:00:00Z",
                  "references": [{"url": "https://example.com"}]
                },
                "security_vulnerability": {
                  "package": {"ecosystem": "npm", "name": "lodash"},
                  "vulnerable_version_range": "< 4.17.21",
                  "first_patched_version": {"identifier": "4.17.21"}
                },
                "created_at": "2024-01-01T00:00:00Z",
                "updated_at": "2024-01-02T00:00:00Z",
                "html_url": "https://github.com/owner/repo/security/dependabot/7"
              }
            }
            """;
        byte[] payload = Encoding.UTF8.GetBytes(body);

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/dependabot");
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Sign(payload, DependabotSecret));

        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        Advisory? row = await feedsDb.Advisories.FirstOrDefaultAsync(entry =>
            entry.ExternalId == "GHSA-test-1234-abcd"
        );
        row.Should().NotBeNull();
        row!.Severity.Should().Be(Severity.High);
        row.PackageName.Should().Be("lodash");
        row.Ecosystem.Should().Be(Ecosystem.Npm);
        row.Feed.Should().Be(Feed.Ghsa);
        row.ReferencesJson.Should().Contain("DEPENDABOT");
    }

    [Fact]
    public async Task Dependabot_with_bad_signature_returns_401_and_persists_nothing()
    {
        await _factory.SetSecretsAsync(GithubSecret, DependabotSecret);
        HttpClient client = _factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/webhooks/dependabot");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + new string('f', 64));
        HttpResponseMessage response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Badge_endpoint_returns_not_watched_svg_when_source_is_missing()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/badge/unknown/repo.svg");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/svg+xml");
        string svg = await response.Content.ReadAsStringAsync();
        svg.Should().Contain("not watched");
    }

    [Fact]
    public async Task Badge_endpoint_returns_finding_counts_for_known_source()
    {
        await using (AsyncServiceScope scope = _factory.Services.CreateAsyncScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            DateTime now = DateTime.UtcNow;
            Source source = new()
            {
                Type = SourceType.GithubRepo,
                Name = "BadgeOwner/BadgeRepo",
                ConfigJson = JsonSerializer.Serialize(
                    new { owner = "BadgeOwner", repo = "BadgeRepo" }
                ),
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync();

            db.Findings.AddRange(
                Finding(source.Id, Severity.Critical, FindingState.Open, now),
                Finding(source.Id, Severity.High, FindingState.Open, now),
                Finding(source.Id, Severity.High, FindingState.Open, now),
                Finding(source.Id, Severity.Low, FindingState.Resolved, now)
            );
            await db.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/badge/BadgeOwner/BadgeRepo.svg");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string svg = await response.Content.ReadAsStringAsync();
        svg.Should().Contain("1C 2H 0M 0L");
    }

    private static Finding Finding(
        int sourceId,
        Severity severity,
        FindingState state,
        DateTime now
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            InventoryItemId = 0,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = severity,
            FirstSeenAt = now,
            LastSeenAt = now,
            State = state,
            DedupKey = Guid.NewGuid().ToString("n"),
        };

    private static string Sign(byte[] payload, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] mac = hmac.ComputeHash(payload);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public sealed class Factory : ShieldWebAppFactory
    {
        private IPrCommentService? _prCommentOverride;

        public void ReplacePrCommentService(IPrCommentService stub)
        {
            _prCommentOverride = stub;
        }

        public async Task SetSecretsAsync(string githubSecret, string dependabotSecret)
        {
            // Touch the factory once so Services is built.
            using HttpClient warmup = CreateClient();
            using HttpResponseMessage _ = await warmup.GetAsync("/healthz");

            IWebhookSecretProvider provider = Services.GetRequiredService<IWebhookSecretProvider>();
            await provider.SaveAsync(githubSecret, dependabotSecret);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IPrCommentServiceFactory>(
                    new PrCommentServiceFactoryAccessor(() => _prCommentOverride)
                );
                // Inject a swap-in so the test can replace the impl AFTER the host is built.
                ServiceDescriptor[] toRemove = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IPrCommentService))
                    .ToArray();
                foreach (ServiceDescriptor descriptor in toRemove)
                    services.Remove(descriptor);
                services.AddScoped<IPrCommentService>(sp =>
                {
                    IPrCommentService? overrideValue =
                        sp.GetRequiredService<IPrCommentServiceFactory>().Current();
                    return overrideValue ?? new NoopPrCommentService();
                });
            });
        }

        private sealed class NoopPrCommentService : IPrCommentService
        {
            public Task<PrCommentResult> ProcessPullRequestAsync(
                string owner,
                string repoName,
                int pullNumber,
                string headRef,
                string baseRef,
                CancellationToken ct
            ) => Task.FromResult(new PrCommentResult(false, 0, 0, null, "test default"));
        }
    }

    public interface IPrCommentServiceFactory
    {
        IPrCommentService? Current();
    }

    private sealed class PrCommentServiceFactoryAccessor : IPrCommentServiceFactory
    {
        private readonly Func<IPrCommentService?> _accessor;

        public PrCommentServiceFactoryAccessor(Func<IPrCommentService?> accessor)
        {
            _accessor = accessor;
        }

        public IPrCommentService? Current() => _accessor();
    }
}
