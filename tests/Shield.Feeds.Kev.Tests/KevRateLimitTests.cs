using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using Shield.Feeds.Kev;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Kev.Tests;

public sealed class KevRateLimitTests : IDisposable
{
    private readonly WireMockServer _server;

    public KevRateLimitTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsync_on_429_returns_RateLimited()
    {
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
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithHeader("Retry-After", "90")
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"kev-rate-{Guid.NewGuid()}")
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

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
