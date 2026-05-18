using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Kev.Tests;

public sealed class KevFeedSyncTests : IDisposable
{
    private readonly WireMockServer _server;

    public KevFeedSyncTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsyncFlagsExistingAdvisoryAndInsertsThinAdvisoryForUnmatchedCve()
    {
        const string catalog = """
            {
              "title": "CISA KEV",
              "catalogVersion": "2026.05.16",
              "count": 2,
              "vulnerabilities": [
                {
                  "cveID": "CVE-2024-12345",
                  "vendorProject": "Acme",
                  "product": "Widget",
                  "vulnerabilityName": "Acme Widget RCE",
                  "dateAdded": "2024-10-01",
                  "shortDescription": "Remote code execution in Acme Widget",
                  "dueDate": "2024-10-22"
                },
                {
                  "cveID": "CVE-2026-99999",
                  "vendorProject": "Nobody",
                  "product": "Mystery",
                  "vulnerabilityName": "Mystery flaw",
                  "dateAdded": "2026-05-01",
                  "shortDescription": "Brand new KEV with no advisory yet",
                  "dueDate": "2026-05-22"
                }
              ]
            }
            """;

        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/sites/default/files/feeds/known_exploited_vulnerabilities.json")
                    .UsingGet()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(catalog)
            );

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"kev-feed-{Guid.NewGuid()}")
            .Options;

        Advisory existing = new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.Osv,
            ExternalId = "CVE-2024-12345",
            Ecosystem = Ecosystem.Npm,
            PackageName = "acme-widget",
            AffectedRangesJson = "[]",
            ReferencesJson = "[]",
            Severity = Severity.Critical,
            Summary = "RCE in acme-widget",
            PublishedAt = DateTime.UtcNow.AddDays(-30),
            ModifiedAt = DateTime.UtcNow.AddDays(-1),
            FetchedAt = DateTime.UtcNow.AddDays(-1),
        };

        await using (FeedsDbContext seed = new(options))
        {
            seed.Advisories.Add(existing);
            await seed.SaveChangesAsync();
        }

        await using FeedsDbContext db = new(options);
        EfKevAdvisoryEnricher enricher = new(db);

        // WireMock served at root — KevFeedSync uses an absolute URL, so override the catalog
        // URL by hijacking the test HttpClient base address. Easiest path: make a delegating
        // handler that rewrites the request URL to point at WireMock.
        UrlRewritingHandler rewriter = new(new(_server.Url!), new HttpClientHandler());
        using HttpClient http = new(rewriter);

        KevFeedSync sync = new(http, enricher);
        FeedSyncResult result = await sync.SyncAsync(
            new() { Feed = Feed.Kev },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();

        await using FeedsDbContext verify = new(options);
        Advisory enriched = await verify.Advisories.FirstAsync(advisory =>
            advisory.ExternalId == "CVE-2024-12345" && advisory.Feed == Feed.Osv
        );
        enriched.IsKev.Should().BeTrue();
        enriched.KevAddedAt.Should().Be(new(2024, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        enriched.KevDueDate.Should().Be(new(2024, 10, 22, 0, 0, 0, DateTimeKind.Utc));

        Advisory thin = await verify.Advisories.FirstAsync(advisory =>
            advisory.ExternalId == "CVE-2026-99999" && advisory.Feed == Feed.Kev
        );
        thin.IsKev.Should().BeTrue();
        thin.PackageName.Should().Be("Mystery");
        thin.Severity.Should().Be(Severity.High);
    }

    [Fact]
    public async Task SyncAsyncReturnsFailureOn5xx()
    {
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/sites/default/files/feeds/known_exploited_vulnerabilities.json")
                    .UsingGet()
            )
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError));

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"kev-feed-fail-{Guid.NewGuid()}")
            .Options;
        await using FeedsDbContext db = new(options);
        EfKevAdvisoryEnricher enricher = new(db);

        UrlRewritingHandler rewriter = new(new(_server.Url!), new HttpClientHandler());
        using HttpClient http = new(rewriter);

        KevFeedSync sync = new(http, enricher);
        FeedSyncResult result = await sync.SyncAsync(
            new() { Feed = Feed.Kev },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void FeedPropertyReturnsKev()
    {
        using HttpClient http = new() { BaseAddress = new("http://localhost/") };
        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"kev-feed-prop-{Guid.NewGuid()}")
            .Options;
        using FeedsDbContext db = new(options);
        EfKevAdvisoryEnricher enricher = new(db);
        KevFeedSync sync = new(http, enricher);
        sync.Feed.Should().Be(Feed.Kev);
    }
}

internal sealed class UrlRewritingHandler : DelegatingHandler
{
    private readonly Uri _base;

    public UrlRewritingHandler(Uri target, HttpMessageHandler inner)
        : base(inner)
    {
        _base = target;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri is not null)
        {
            UriBuilder rebuilt = new(request.RequestUri)
            {
                Scheme = _base.Scheme,
                Host = _base.Host,
                Port = _base.Port,
            };
            request.RequestUri = rebuilt.Uri;
        }
        return base.SendAsync(request, cancellationToken);
    }
}
