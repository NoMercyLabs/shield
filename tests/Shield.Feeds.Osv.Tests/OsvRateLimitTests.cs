using System.Net;
using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.Osv.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Osv.Tests;

public sealed class OsvRateLimitTests : IDisposable
{
    private readonly WireMockServer _server;

    public OsvRateLimitTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task QueryBatchOn429FromQuerybatchReturnsRateLimited()
    {
        _server
            .Given(Request.Create().WithPath("/v1/querybatch").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithHeader("Retry-After", "120")
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        using HttpClient http = new() { BaseAddress = new(_server.Url!) };
        OsvFeedSync sync = new(http);
        FeedSyncState state = new() { Feed = Feed.Osv, Cursor = "2026-01-01T00:00:00Z" };
        OsvQuery[] queries = [new(Ecosystem.Npm, "lodash", "4.17.20")];

        (IReadOnlyList<Advisory> advisories, FeedSyncResult result) = await sync.QueryBatchAsync(
            state,
            queries,
            CancellationToken.None
        );

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
        advisories.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryBatchOn429WithoutRetryAfterUsesFallbackReset()
    {
        _server
            .Given(Request.Create().WithPath("/v1/querybatch").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        using HttpClient http = new() { BaseAddress = new(_server.Url!) };
        OsvFeedSync sync = new(http);
        FeedSyncState state = new() { Feed = Feed.Osv };
        OsvQuery[] queries = [new(Ecosystem.Npm, "lodash", "4.17.20")];

        (IReadOnlyList<Advisory> _, FeedSyncResult result) = await sync.QueryBatchAsync(
            state,
            queries,
            CancellationToken.None
        );

        result.IsRateLimited.Should().BeTrue();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
