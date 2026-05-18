using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.Ghsa;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Ghsa.Tests;

public sealed class GhsaFeedSyncTests : IDisposable
{
    private readonly WireMockServer _server;

    public GhsaFeedSyncTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsync_parses_five_advisories_and_advances_cursor()
    {
        const string graphqlBody = """
            {
              "data": {
                "securityAdvisories": {
                  "pageInfo": { "hasNextPage": false, "endCursor": null },
                  "nodes": [
                    {
                      "ghsaId": "GHSA-aaaa-aaaa-aaaa",
                      "summary": "Prototype pollution",
                      "severity": "HIGH",
                      "publishedAt": "2026-05-01T10:00:00Z",
                      "updatedAt": "2026-05-01T11:00:00Z",
                      "references": [{ "url": "https://github.com/advisories/GHSA-aaaa-aaaa-aaaa" }],
                      "cvss": { "score": 8.1 },
                      "vulnerabilities": {
                        "nodes": [
                          {
                            "package": { "ecosystem": "NPM", "name": "lodash" },
                            "vulnerableVersionRange": "< 4.17.21",
                            "firstPatchedVersion": { "identifier": "4.17.21" }
                          }
                        ]
                      }
                    },
                    {
                      "ghsaId": "GHSA-bbbb-bbbb-bbbb",
                      "summary": "SSRF",
                      "severity": "MODERATE",
                      "publishedAt": "2026-05-02T10:00:00Z",
                      "updatedAt": "2026-05-02T11:00:00Z",
                      "references": [],
                      "cvss": { "score": 5.0 },
                      "vulnerabilities": {
                        "nodes": [
                          {
                            "package": { "ecosystem": "NUGET", "name": "Some.Pkg" },
                            "vulnerableVersionRange": ">= 1.0.0, < 1.2.3",
                            "firstPatchedVersion": { "identifier": "1.2.3" }
                          }
                        ]
                      }
                    },
                    {
                      "ghsaId": "GHSA-cccc-cccc-cccc",
                      "summary": "DoS",
                      "severity": "LOW",
                      "publishedAt": "2026-05-03T10:00:00Z",
                      "updatedAt": "2026-05-03T11:00:00Z",
                      "references": [],
                      "cvss": null,
                      "vulnerabilities": {
                        "nodes": [
                          {
                            "package": { "ecosystem": "COMPOSER", "name": "vendor/pkg" },
                            "vulnerableVersionRange": "< 2.0.0",
                            "firstPatchedVersion": { "identifier": "2.0.0" }
                          }
                        ]
                      }
                    },
                    {
                      "ghsaId": "GHSA-dddd-dddd-dddd",
                      "summary": "RCE",
                      "severity": "CRITICAL",
                      "publishedAt": "2026-05-04T10:00:00Z",
                      "updatedAt": "2026-05-04T11:00:00Z",
                      "references": [],
                      "cvss": { "score": 9.8 },
                      "vulnerabilities": {
                        "nodes": [
                          {
                            "package": { "ecosystem": "MAVEN", "name": "org.example:lib" },
                            "vulnerableVersionRange": "< 3.1.0",
                            "firstPatchedVersion": { "identifier": "3.1.0" }
                          }
                        ]
                      }
                    },
                    {
                      "ghsaId": "GHSA-eeee-eeee-eeee",
                      "summary": "Info disclosure",
                      "severity": "HIGH",
                      "publishedAt": "2026-05-05T10:00:00Z",
                      "updatedAt": "2026-05-05T11:00:00Z",
                      "references": [],
                      "cvss": { "score": 7.5 },
                      "vulnerabilities": {
                        "nodes": [
                          {
                            "package": { "ecosystem": "NPM", "name": "axios" },
                            "vulnerableVersionRange": "< 1.6.0",
                            "firstPatchedVersion": { "identifier": "1.6.0" }
                          }
                        ]
                      }
                    }
                  ]
                }
              }
            }
            """;

        _server
            .Given(Request.Create().WithPath("/graphql").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(graphqlBody)
            );

        HttpClient http = new() { BaseAddress = new($"{_server.Url}/graphql") };
        GhsaGraphQLClient client = new(http);
        InMemoryAdvisorySink sink = new();
        GhsaOptions options = new() { PageSize = 100 };
        GhsaFeedSync feedSync = new(
            client,
            sink,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        FeedSyncState state = new() { Feed = Feed.Ghsa, Cursor = "2026-04-01T00:00:00Z" };
        FeedSyncResult result = await feedSync.SyncAsync(state, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AdvisoriesIngested.Should().Be(5);
        sink.Advisories.Should().HaveCount(5);
        sink.Advisories.Select(advisory => advisory.ExternalId)
            .Should()
            .Contain(
                [
                    "GHSA-aaaa-aaaa-aaaa",
                    "GHSA-bbbb-bbbb-bbbb",
                    "GHSA-cccc-cccc-cccc",
                    "GHSA-dddd-dddd-dddd",
                    "GHSA-eeee-eeee-eeee"
                ]
            );

        result.NextCursor.Should().NotBeNull();
        DateTime cursorParsed = DateTime.Parse(
            result.NextCursor!,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal
        );
        cursorParsed.Should().Be(new(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc));
        state.Cursor.Should().Be(result.NextCursor);
    }

    [Fact]
    public void Feed_property_returns_Ghsa()
    {
        HttpClient http = new() { BaseAddress = new("http://localhost/graphql") };
        GhsaGraphQLClient client = new(http);
        InMemoryAdvisorySink sink = new();
        GhsaOptions options = new();
        GhsaFeedSync feedSync = new(
            client,
            sink,
            Microsoft.Extensions.Options.Options.Create(options)
        );

        feedSync.Feed.Should().Be(Feed.Ghsa);
    }

    [Fact]
    public void MapSeverity_moderate_to_medium()
    {
        GhsaMapping.MapSeverity("MODERATE").Should().Be(Severity.Medium);
        GhsaMapping.MapSeverity("LOW").Should().Be(Severity.Low);
        GhsaMapping.MapSeverity("HIGH").Should().Be(Severity.High);
        GhsaMapping.MapSeverity("CRITICAL").Should().Be(Severity.Critical);
    }

    [Fact]
    public void MapEcosystem_maps_known_ecosystems()
    {
        GhsaMapping.MapEcosystem("NPM").Should().Be(Ecosystem.Npm);
        GhsaMapping.MapEcosystem("NUGET").Should().Be(Ecosystem.Nuget);
        GhsaMapping.MapEcosystem("COMPOSER").Should().Be(Ecosystem.Composer);
        GhsaMapping.MapEcosystem("MAVEN").Should().Be(Ecosystem.Gradle);
        GhsaMapping.MapEcosystem("PIP").Should().BeNull();
    }
}
