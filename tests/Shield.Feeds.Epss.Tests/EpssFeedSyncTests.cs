using System.IO.Compression;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Feeds.Epss.Tests;

public sealed class EpssFeedSyncTests : IDisposable
{
    private readonly WireMockServer _server;

    public EpssFeedSyncTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task SyncAsyncStreamsCsvAndUpdatesScoreAndPercentileOnMatchingAdvisories()
    {
        string csv = string.Concat(
            "#model_version:v2025.03.14,score_date:2026-05-15T00:00:00+0000\n",
            "cve,epss,percentile\n",
            "CVE-2020-8203,0.84321,0.99102\n",
            "CVE-2019-10744,0.12345,0.42500\n",
            "CVE-2024-99999,0.00012,0.00100\n"
        );

        byte[] gzipBytes = Gzip(csv);

        _server
            .Given(Request.Create().WithPath("/epss_scores-current.csv.gz").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithHeader("Content-Type", "application/gzip")
                    .WithBody(gzipBytes)
            );

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"epss-feed-{Guid.NewGuid()}")
            .Options;

        await using (FeedsDbContext seed = new(options))
        {
            seed.Advisories.AddRange(
                MakeAdvisory("CVE-2020-8203", "lodash"),
                MakeAdvisory("CVE-2019-10744", "lodash"),
                // CVE-2024-99999 not seeded — EPSS does not insert thin advisories.
                MakeAdvisory("CVE-2018-0000", "left-pad")
            );
            await seed.SaveChangesAsync();
        }

        await using FeedsDbContext db = new(options);
        EfEpssAdvisoryEnricher enricher = new(db);

        UrlRewritingHandler rewriter = new(new(_server.Url!), new HttpClientHandler());
        using HttpClient http = new(rewriter);

        EpssFeedSync sync = new(http, enricher);
        FeedSyncResult result = await sync.SyncAsync(
            new() { Feed = Feed.Epss },
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.AdvisoriesUpdated.Should().Be(2);

        await using FeedsDbContext verify = new(options);
        Advisory lodash8203 = await verify.Advisories.FirstAsync(advisory =>
            advisory.ExternalId == "CVE-2020-8203"
        );
        lodash8203.EpssScore.Should().BeApproximately(0.84321, 1e-6);
        lodash8203.EpssPercentile.Should().BeApproximately(0.99102, 1e-6);

        Advisory lodash10744 = await verify.Advisories.FirstAsync(advisory =>
            advisory.ExternalId == "CVE-2019-10744"
        );
        lodash10744.EpssScore.Should().BeApproximately(0.12345, 1e-6);

        Advisory leftPad = await verify.Advisories.FirstAsync(advisory =>
            advisory.ExternalId == "CVE-2018-0000"
        );
        leftPad.EpssScore.Should().BeNull();

        // EPSS must NOT insert thin advisories for unmatched CVEs.
        (await verify.Advisories.AnyAsync(advisory => advisory.ExternalId == "CVE-2024-99999"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task CsvParserSkipsHeaderAndCommentLines()
    {
        string csv = string.Concat(
            "#model_version:v2\n",
            "cve,epss,percentile\n",
            "CVE-2024-0001,0.5,0.9\n",
            "\n",
            "CVE-2024-0002,0.1,0.5\n"
        );
        using MemoryStream gz = new(Gzip(csv));

        List<EpssEntry> rows = [];
        await foreach (EpssEntry entry in EpssCsvParser.ReadAsync(gz, CancellationToken.None))
        {
            rows.Add(entry);
        }

        rows.Should().HaveCount(2);
        rows[0].CveId.Should().Be("CVE-2024-0001");
        rows[0].Score.Should().Be(0.5);
        rows[1].CveId.Should().Be("CVE-2024-0002");
    }

    [Fact]
    public async Task SyncAsyncReturnsFailureOn5xx()
    {
        _server
            .Given(Request.Create().WithPath("/epss_scores-current.csv.gz").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.InternalServerError));

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"epss-feed-fail-{Guid.NewGuid()}")
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

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void FeedPropertyReturnsEpss()
    {
        using HttpClient http = new() { BaseAddress = new("http://localhost/") };
        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseInMemoryDatabase($"epss-feed-prop-{Guid.NewGuid()}")
            .Options;
        using FeedsDbContext db = new(options);
        EfEpssAdvisoryEnricher enricher = new(db);
        EpssFeedSync sync = new(http, enricher);
        sync.Feed.Should().Be(Feed.Epss);
    }

    private static byte[] Gzip(string text)
    {
        using MemoryStream output = new();
        using (GZipStream gz = new(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            gz.Write(payload, 0, payload.Length);
        }
        return output.ToArray();
    }

    private static Advisory MakeAdvisory(string cve, string packageName) =>
        new()
        {
            Id = Guid.NewGuid(),
            Feed = Feed.Osv,
            ExternalId = cve,
            Ecosystem = Ecosystem.Npm,
            PackageName = packageName,
            AffectedRangesJson = "[]",
            ReferencesJson = "[]",
            Severity = Severity.High,
            Summary = $"Issue in {packageName}",
            PublishedAt = DateTime.UtcNow.AddDays(-30),
            ModifiedAt = DateTime.UtcNow.AddDays(-1),
            FetchedAt = DateTime.UtcNow.AddDays(-1),
        };
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
