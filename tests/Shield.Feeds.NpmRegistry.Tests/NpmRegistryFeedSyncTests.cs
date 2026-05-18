using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.NpmRegistry;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.NpmRegistry.Tests;

public sealed class NpmRegistryFeedSyncTests : IDisposable
{
    private readonly WireMockServer _server;

    public NpmRegistryFeedSyncTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsync_extracts_time_maintainers_deprecated_and_shasum()
    {
        const string packageDoc = """
            {
              "name": "left-pad",
              "time": {
                "1.0.0": "2024-01-10T12:00:00Z",
                "1.1.0": "2024-02-15T09:30:00Z"
              },
              "maintainers": [
                { "name": "azer", "email": "azer@example.com" },
                { "name": "kik", "email": "kik@example.com" }
              ],
              "dist-tags": {
                "latest": "1.1.0"
              },
              "versions": {
                "1.0.0": {
                  "dist": { "shasum": "abc123", "tarball": "https://registry.npmjs.org/left-pad/-/left-pad-1.0.0.tgz" }
                },
                "1.1.0": {
                  "dist": { "shasum": "def456", "tarball": "https://registry.npmjs.org/left-pad/-/left-pad-1.1.0.tgz" },
                  "deprecated": "use String.prototype.padStart instead"
                }
              }
            }
            """;

        _server
            .Given(Request.Create().WithPath("/left-pad").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(packageDoc)
            );

        HttpClient http = new() { BaseAddress = new(_server.Url!.TrimEnd('/') + "/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new(["left-pad"]);
        NpmRegistryOptions options = new() { MaxRequestsPerSecond = 50 };
        using NpmRegistryFeedSync feedSync = new(
            client,
            sink,
            nameSource,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        FeedSyncState state = new() { Feed = Feed.NpmRegistry };
        FeedSyncResult result = await feedSync.SyncAsync(state, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AdvisoriesUpdated.Should().Be(2);
        sink.Packages.Should().HaveCount(2);

        PackageMeta v1 = sink.Packages.Single(meta => meta.Version == "1.0.0");
        v1.Ecosystem.Should().Be(Ecosystem.Npm);
        v1.Name.Should().Be("left-pad");
        v1.TarballSha.Should().Be("abc123");
        v1.Deprecated.Should().BeFalse();
        v1.PublishedAt.Should().Be(new(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        v1.MaintainersJson.Should().Contain("azer").And.Contain("kik");

        PackageMeta v11 = sink.Packages.Single(meta => meta.Version == "1.1.0");
        v11.TarballSha.Should().Be("def456");
        v11.Deprecated.Should().BeTrue();
        v11.PublishedAt.Should().Be(new(2024, 2, 15, 9, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SyncAsync_returns_success_on_missing_package()
    {
        _server
            .Given(Request.Create().WithPath("/does-not-exist").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.NotFound));

        HttpClient http = new() { BaseAddress = new(_server.Url!.TrimEnd('/') + "/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new(["does-not-exist"]);
        NpmRegistryOptions options = new();
        using NpmRegistryFeedSync feedSync = new(
            client,
            sink,
            nameSource,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        FeedSyncResult result = await feedSync.SyncAsync(
            new() { Feed = Feed.NpmRegistry },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.AdvisoriesUpdated.Should().Be(0);
        sink.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncAsync_returns_failure_on_5xx()
    {
        _server
            .Given(Request.Create().WithPath("/boom").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError));

        HttpClient http = new() { BaseAddress = new(_server.Url!.TrimEnd('/') + "/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new(["boom"]);
        NpmRegistryOptions options = new();
        using NpmRegistryFeedSync feedSync = new(
            client,
            sink,
            nameSource,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        FeedSyncResult result = await feedSync.SyncAsync(
            new() { Feed = Feed.NpmRegistry },
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void Feed_property_returns_NpmRegistry()
    {
        HttpClient http = new() { BaseAddress = new("http://localhost/") };
        NpmPackageClient client = new(http);
        InMemoryPackageMetaSink sink = new();
        InMemoryPackageNameSource nameSource = new([]);
        NpmRegistryOptions options = new();
        using NpmRegistryFeedSync feedSync = new(
            client,
            sink,
            nameSource,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        feedSync.Feed.Should().Be(Feed.NpmRegistry);
    }
}
