using System.Globalization;
using System.Net;
using FluentAssertions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.Osv;
using Shield.Feeds.Osv.Extensions;
using Shield.Feeds.Osv.Models;
using Xunit;

namespace Shield.Feeds.Osv.Tests;

public class OsvFeedSyncTests
{
    [Fact]
    public async Task QueryBatch_ReturnsAdvisories_ForNpmAndNugetVulns()
    {
        using OsvServerFixture fixture = new();
        fixture.StubBatch(OsvServerFixture.ReadFixture("querybatch-response.json"));
        fixture.StubVuln("GHSA-npm-critical", OsvServerFixture.ReadFixture("vuln-npm-critical.json"));
        fixture.StubVuln("GHSA-nuget-high", OsvServerFixture.ReadFixture("vuln-nuget-high.json"));

        OsvFeedSync sync = new(fixture.Client);
        FeedSyncState state = new() { Feed = Feed.Osv };
        IReadOnlyList<OsvQuery> queries =
        [
            new(Ecosystem.Npm, "lodash", "4.17.20"),
            new(Ecosystem.Nuget, "Some.Nuget.Package", "1.2.0"),
            new(Ecosystem.Npm, "clean", "1.0.0")
        ];

        (IReadOnlyList<Advisory> advisories, FeedSyncResult result) = await sync.QueryBatchAsync(state, queries, CancellationToken.None);

        advisories.Should().HaveCount(2);
        advisories.Should().Contain(a => a.ExternalId == "GHSA-npm-critical" && a.Ecosystem == Ecosystem.Npm && a.PackageName == "lodash" && a.Severity == Severity.Critical);
        advisories.Should().Contain(a => a.ExternalId == "GHSA-nuget-high" && a.Ecosystem == Ecosystem.Nuget && a.PackageName == "Some.Nuget.Package" && a.Severity == Severity.High);
        result.Error.Should().BeNull();
        result.AdvisoriesIngested.Should().Be(2);
    }

    [Theory]
    [InlineData("vuln-npm-critical.json", Severity.Critical, 9.8)]
    [InlineData("vuln-nuget-high.json", Severity.High, 7.5)]
    [InlineData("vuln-medium-no-score.json", Severity.Medium, null)]
    [InlineData("vuln-low-default.json", Severity.Low, null)]
    public async Task SeverityMapping_CoversAllFourLevels(string fixtureFile, Severity expected, double? expectedCvss)
    {
        using OsvServerFixture fixture = new();
        string vulnJson = OsvServerFixture.ReadFixture(fixtureFile);
        string vulnId = ExtractId(vulnJson);
        string batchBody = $"{{\"results\":[{{\"vulns\":[{{\"id\":\"{vulnId}\",\"modified\":\"2026-04-10T12:00:00Z\"}}]}}]}}";

        fixture.StubBatch(batchBody);
        fixture.StubVuln(vulnId, vulnJson);

        OsvFeedSync sync = new(fixture.Client);
        FeedSyncState state = new() { Feed = Feed.Osv };
        OsvQuery[] queries = [new(Ecosystem.Npm, "any", "1.0.0")];

        (IReadOnlyList<Advisory> advisories, FeedSyncResult _) = await sync.QueryBatchAsync(state, queries, CancellationToken.None);

        advisories.Should().NotBeEmpty();
        advisories[0].Severity.Should().Be(expected);
        if (expectedCvss is not null) advisories[0].Cvss.Should().Be(expectedCvss);
        else advisories[0].Cvss.Should().BeNull();
    }

    [Fact]
    public async Task IncrementalSync_AdvancesCursor_ToLatestModifiedTimestamp()
    {
        using OsvServerFixture fixture = new();
        fixture.StubBatch(OsvServerFixture.ReadFixture("querybatch-response.json"));
        fixture.StubVuln("GHSA-npm-critical", OsvServerFixture.ReadFixture("vuln-npm-critical.json"));
        fixture.StubVuln("GHSA-nuget-high", OsvServerFixture.ReadFixture("vuln-nuget-high.json"));

        OsvFeedSync sync = new(fixture.Client);
        DateTime initialCursor = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        FeedSyncState state = new()
        {
            Feed = Feed.Osv,
            Cursor = initialCursor.ToString("O", CultureInfo.InvariantCulture)
        };
        OsvQuery[] queries = [new(Ecosystem.Npm, "lodash", "4.17.20")];

        (IReadOnlyList<Advisory> _, FeedSyncResult result) = await sync.QueryBatchAsync(state, queries, CancellationToken.None);

        result.NextCursor.Should().NotBeNull();
        DateTime parsed = DateTime.Parse(result.NextCursor!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        parsed.Should().Be(new(2026, 5, 1, 8, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Retries_OnHttp429_ViaPollyPolicy()
    {
        using OsvServerFixture fixture = new();
        string batchBody = "{\"results\":[{\"vulns\":[{\"id\":\"GHSA-npm-critical\",\"modified\":\"2026-04-10T12:00:00Z\"}]}]}";
        fixture.StubBatch(batchBody);
        fixture.StubVuln("GHSA-npm-critical", OsvServerFixture.ReadFixture("vuln-npm-critical.json"));

        FailNTimesHandler failer = new(2, HttpStatusCode.TooManyRequests, "/v1/querybatch")
        {
            InnerHandler = new HttpClientHandler()
        };
        PollyHttpRetryHandler polly = new() { InnerHandler = failer };

        using HttpClient client = new(polly) { BaseAddress = fixture.Client.BaseAddress };

        OsvFeedSync sync = new(client);
        FeedSyncState state = new() { Feed = Feed.Osv };
        OsvQuery[] queries = [new(Ecosystem.Npm, "lodash", "4.17.20")];

        (IReadOnlyList<Advisory> advisories, FeedSyncResult result) = await sync.QueryBatchAsync(state, queries, CancellationToken.None);

        result.Error.Should().BeNull();
        advisories.Should().ContainSingle(a => a.ExternalId == "GHSA-npm-critical");
        failer.FailureCalls.Should().Be(2);
        failer.SuccessCalls.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_IsNoOp_ReturnsCurrentCursor()
    {
        using OsvServerFixture fixture = new();
        OsvFeedSync sync = new(fixture.Client);
        FeedSyncState state = new() { Feed = Feed.Osv, Cursor = "anchor-cursor" };

        FeedSyncResult result = await sync.SyncAsync(state, CancellationToken.None);

        result.NextCursor.Should().Be("anchor-cursor");
        result.Error.Should().BeNull();
        result.AdvisoriesIngested.Should().Be(0);
    }

    [Fact]
    public async Task SyncAllAsync_ThrowsNotImplemented_InPhase1()
    {
        using OsvServerFixture fixture = new();
        OsvFeedSync sync = new(fixture.Client);
        FeedSyncState state = new() { Feed = Feed.Osv };

        Func<Task> act = async () => await sync.SyncAllAsync(state, CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void Feed_Property_IsOsv()
    {
        using OsvServerFixture fixture = new();
        OsvFeedSync sync = new(fixture.Client);
        sync.Feed.Should().Be(Feed.Osv);
    }

    private static string ExtractId(string json)
    {
        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
