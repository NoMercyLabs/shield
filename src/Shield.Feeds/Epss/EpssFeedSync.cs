using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.Epss;

public sealed class EpssFeedSync : IFeedSync
{
    public const string HttpClientName = "epss";

    // Cyentia rebranded to Empirical Security in 2026. The legacy host still serves a
    // 301 redirect but .NET's HttpClientFactory pipeline hangs on the gzip-decompression
    // handler chain when chasing the redirect, leaving every EPSS sync stalled with zero
    // log signal. Pointing directly at the new origin avoids the dance entirely. Update
    // this constant if Empirical ever moves again.
    public const string CsvUrl =
        "https://epss.empiricalsecurity.com/epss_scores-current.csv.gz";
    public const int BatchSize = 500;

    private readonly HttpClient _http;
    private readonly IEpssAdvisoryEnricher _enricher;
    private readonly TimeProvider _time;
    private readonly ILogger<EpssFeedSync> _logger;

    public EpssFeedSync(
        HttpClient http,
        IEpssAdvisoryEnricher enricher,
        TimeProvider? time = null,
        ILogger<EpssFeedSync>? logger = null
    )
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _enricher = enricher ?? throw new ArgumentNullException(nameof(enricher));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<EpssFeedSync>.Instance;
    }

    public Feed Feed => Feed.Epss;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(CsvUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                throw new EpssRateLimitedException(ParseRetryAfter(response));

            response.EnsureSuccessStatusCode();

            await using Stream body = await response
                .Content.ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

            int totalRead = 0;
            int totalUpdated = 0;
            List<EpssEntry> batch = new(BatchSize);

            await foreach (EpssEntry entry in EpssCsvParser.ReadAsync(body, ct))
            {
                batch.Add(entry);
                totalRead++;

                if (batch.Count >= BatchSize)
                {
                    totalUpdated += await _enricher.ApplyBatchAsync(batch, ct);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                totalUpdated += await _enricher.ApplyBatchAsync(batch, ct);

            string cursor = _time.GetUtcNow().UtcDateTime.ToString("O");
            _logger.LogInformation(
                "EPSS sync: rows read={Read} advisories updated={Updated}",
                totalRead,
                totalUpdated
            );

            return FeedSyncResult.Ok(0, totalUpdated, cursor);
        }
        catch (EpssRateLimitedException ex)
        {
            _logger.LogInformation("EPSS rate-limited; next sync at {RetryAt:u}", ex.RetryAt);
            return FeedSyncResult.RateLimited(ex.RetryAt, state.Cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "EPSS sync failed");
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
