using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using Shield.Feeds.Epss;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Epss.Tests;

public sealed class EpssRateLimitTests : IDisposable
{
    private readonly WireMockServer _server;

    public EpssRateLimitTests()
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
            .Given(Request.Create().WithPath("/epss_scores-current.csv.gz").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithHeader("Retry-After", "30")
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"epss-rate-{Guid.NewGuid()}")
            .Options;
        await using FeedsDbContext db = new(options);
        EfEpssAdvisoryEnricher enricher = new(db);

        UrlRewritingHandler rewriter = new(new(_server.Url!), new HttpClientHandler());
        using HttpClient http = new(rewriter);

        EpssFeedSync sync = new(http, enricher);
        FeedSyncResult result = await sync.SyncAsync(
            new() { Feed = Feed.Epss },
            CancellationToken.None
        );

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
