using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.NpmRegistry.Tests;

public sealed class NpmRegistryRateLimitTests : IDisposable
{
    private readonly WireMockServer _server;

    public NpmRegistryRateLimitTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsyncOn429ReturnsRateLimited()
    {
        _server
            .Given(Request.Create().WithPath("/lodash").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithHeader("Retry-After", "60")
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        HttpClient http = new() { BaseAddress = new(_server.Url!.TrimEnd('/') + "/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new(["lodash"]);
        NpmRegistryOptions options = new() { MaxRequestsPerSecond = 50 };
        using NpmRegistryFeedSync feedSync = new(client, sink, nameSource, Options.Create(options));

        FeedSyncResult result = await feedSync.SyncAsync(
            new() { Feed = Feed.NpmRegistry },
            CancellationToken.None
        );

        result.IsRateLimited.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
        sink.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsyncOn429WithoutRetryAfterUsesFallbackReset()
    {
        _server
            .Given(Request.Create().WithPath("/express").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.TooManyRequests)
                    .WithBody("{\"message\":\"rate limit exceeded\"}")
            );

        HttpClient http = new() { BaseAddress = new(_server.Url!.TrimEnd('/') + "/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new(["express"]);
        NpmRegistryOptions options = new();
        using NpmRegistryFeedSync feedSync = new(client, sink, nameSource, Options.Create(options));

        FeedSyncResult result = await feedSync.SyncAsync(
            new() { Feed = Feed.NpmRegistry },
            CancellationToken.None
        );

        result.IsRateLimited.Should().BeTrue();
        result.RateLimitResetAt.Should().NotBeNull();
        result.RateLimitResetAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }
}
