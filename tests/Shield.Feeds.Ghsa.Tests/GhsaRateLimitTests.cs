using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Ghsa.Tests;

public sealed class GhsaRateLimitTests : IDisposable
{
    private readonly WireMockServer _server;

    public GhsaRateLimitTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsyncOn403WithRateLimitHeaderReturnsRateLimited()
    {
        long resetEpoch = DateTimeOffset.UtcNow.AddMinutes(45).ToUnixTimeSeconds();

        _server
            .Given(Request.Create().WithPath("/graphql").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.Forbidden)
                    .WithHeader("X-RateLimit-Remaining", "0")
                    .WithHeader(
                        "X-RateLimit-Reset",
                        resetEpoch.ToString(CultureInfo.InvariantCulture)
                    )
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        HttpClient http = new() { BaseAddress = new($"{_server.Url}/graphql") };
        GhsaGraphQLClient client = new(http);
        InMemoryAdvisorySink sink = new();
        GhsaOptions options = new() { PageSize = 100 };
        GhsaFeedSync feedSync = new(client, sink, Options.Create(options));

        FeedSyncState state = new() { Feed = Feed.Ghsa, Cursor = "2026-04-01T00:00:00Z" };
        FeedSyncResult result = await feedSync.SyncAsync(state, CancellationToken.None);

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.ToUnixTimeSeconds().Should().Be(resetEpoch);
        sink.Advisories.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsyncOn403WithoutRateLimitHeaderReturnsRateLimitedWithFallbackReset()
    {
        _server
            .Given(Request.Create().WithPath("/graphql").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.Forbidden)
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        HttpClient http = new() { BaseAddress = new($"{_server.Url}/graphql") };
        GhsaGraphQLClient client = new(http);
        InMemoryAdvisorySink sink = new();
        GhsaOptions options = new() { PageSize = 100 };
        GhsaFeedSync feedSync = new(client, sink, Options.Create(options));

        FeedSyncState state = new() { Feed = Feed.Ghsa, Cursor = null };
        FeedSyncResult result = await feedSync.SyncAsync(state, CancellationToken.None);

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void IsTransientReturnsFalseFor403WithoutRateLimitRemainingZero()
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "10");

        PollyTransientHandler.IsTransient(response).Should().BeFalse();
    }

    [Fact]
    public void IsTransientReturnsTrueFor403WithRemainingZero()
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");

        PollyTransientHandler.IsTransient(response).Should().BeTrue();
    }

    [Fact]
    public void IsTransientReturnsFalseFor403WithNoRateLimitHeaders()
    {
        HttpResponseMessage response = new(HttpStatusCode.Forbidden);

        PollyTransientHandler.IsTransient(response).Should().BeFalse();
    }

    [Fact]
    public void FeedSyncResultRateLimitedFactorySetsExpectedFields()
    {
        DateTimeOffset resetAt = DateTimeOffset.UtcNow.AddHours(1);
        FeedSyncResult result = FeedSyncResult.RateLimited(resetAt, cursor: "some-cursor");

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().Be(resetAt);
        result.NextCursor.Should().Be("some-cursor");
        result.AdvisoriesIngested.Should().Be(0);
    }
}
