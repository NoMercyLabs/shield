using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.Kev.Models;

namespace Shield.Feeds.Kev;

public sealed class KevFeedSync : IFeedSync
{
    public const string HttpClientName = "kev";
    public const string CatalogUrl =
        "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";

    private readonly HttpClient _http;
    private readonly IKevAdvisoryEnricher _enricher;
    private readonly TimeProvider _time;
    private readonly ILogger<KevFeedSync> _logger;

    public KevFeedSync(
        HttpClient http,
        IKevAdvisoryEnricher enricher,
        TimeProvider? time = null,
        ILogger<KevFeedSync>? logger = null
    )
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<KevFeedSync>.Instance;
    }

    public Feed Feed => Feed.Kev;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage check = await _http
                .GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (check.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new KevRateLimitedException(ParseRetryAfter(check));

            check.EnsureSuccessStatusCode();

            KevCatalogDocument? document = await check
                .Content.ReadFromJsonAsync<KevCatalogDocument>(ct)
                .ConfigureAwait(false);

            if (document is null || document.Vulnerabilities.Count == 0)
            {
                _logger.LogWarning("KEV catalog response was empty");
                return FeedSyncResult.Ok(0, 0, state.Cursor);
            }

            List<KevCatalogEntry> entries = document
                .Vulnerabilities.Where(vuln => !string.IsNullOrWhiteSpace(vuln.CveId))
                .Select(vuln => new KevCatalogEntry(
                    vuln.CveId,
                    vuln.DateAdded,
                    vuln.DueDate,
                    vuln.VendorProject,
                    vuln.Product,
                    vuln.VulnerabilityName,
                    vuln.ShortDescription
                ))
                .ToList();

            KevEnrichmentResult result = await _enricher.ApplyAsync(entries, ct);
            string cursor = (
                document.CatalogVersion ?? _time.GetUtcNow().UtcDateTime.ToString("O")
            );

            _logger.LogInformation(
                "KEV sync: catalog {Version} count={Count} updated={Updated} inserted={Inserted}",
                document.CatalogVersion,
                entries.Count,
                result.Updated,
                result.Inserted
            );

            return FeedSyncResult.Ok(result.Inserted, result.Updated, cursor);
        }
        catch (KevRateLimitedException ex)
        {
            _logger.LogInformation("KEV rate-limited; next sync at {RetryAt:u}", ex.RetryAt);
            return FeedSyncResult.RateLimited(ex.RetryAt, state.Cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "KEV catalog fetch failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }

    private static DateTimeOffset ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta.HasValue)
                return DateTimeOffset.UtcNow + header.Delta.Value;
            if (header.Date.HasValue)
                return header.Date.Value;
        }

        return DateTimeOffset.UtcNow.AddMinutes(5);
    }
}
