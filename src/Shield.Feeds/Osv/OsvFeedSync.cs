using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Feeds.Osv.Models;

namespace Shield.Feeds.Osv;

public sealed class OsvFeedSync : IFeedSync
{
    public const string HttpClientName = "osv";
    public const int MaxBatchSize = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Used for AffectedRangesJson + ReferencesJson persistence — drops null fields so the
    // matcher's range parser (which uses TryGetProperty semantics: "absent" vs "present-null")
    // sees the OSV shape without false positives on the introduced/fixed branch detection.
    private static readonly JsonSerializerOptions PersistedJsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ILogger<OsvFeedSync> _logger;

    public OsvFeedSync(HttpClient http, ILogger<OsvFeedSync>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? NullLogger<OsvFeedSync>.Instance;
    }

    public Feed Feed => Feed.Osv;

    public ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct) =>
        new(FeedSyncResult.Ok(0, 0, state.Cursor));

    public ValueTask<FeedSyncResult> SyncAllAsync(FeedSyncState state, CancellationToken ct) =>
        throw new NotImplementedException(
            "Full mirror sync from osv-vulnerabilities/all.zip lands in Phase 2."
        );

    public ValueTask<(IReadOnlyList<Advisory> Advisories, FeedSyncResult Result)> QueryBatchAsync(
        FeedSyncState state,
        IReadOnlyList<OsvQuery> queries,
        CancellationToken ct
    ) => QueryBatchAsync(state, queries, knownExternalIds: null, ct);

    public async ValueTask<(
        IReadOnlyList<Advisory> Advisories,
        FeedSyncResult Result
    )> QueryBatchAsync(
        FeedSyncState state,
        IReadOnlyList<OsvQuery> queries,
        IReadOnlySet<string>? knownExternalIds,
        CancellationToken ct
    )
    {
        if (queries.Count == 0)
            return (Array.Empty<Advisory>(), FeedSyncResult.Ok(0, 0, state.Cursor));

        try
        {
            List<Advisory> collected = new();
            DateTime? maxModified = TryParseCursor(state.Cursor);
            int skipped = 0;

            foreach (IEnumerable<OsvQuery> batch in Chunk(queries, MaxBatchSize))
            {
                IReadOnlyList<string> vulnIds = await PostBatchAsync(batch, ct)
                    .ConfigureAwait(false);
                foreach (string vulnId in vulnIds.Distinct(StringComparer.Ordinal))
                {
                    if (knownExternalIds is not null && knownExternalIds.Contains(vulnId))
                    {
                        skipped++;
                        continue;
                    }

                    OsvVulnerability? vuln = await GetVulnAsync(vulnId, ct).ConfigureAwait(false);
                    if (vuln is null)
                        continue;

                    foreach (Advisory advisory in NormalizeVuln(vuln))
                    {
                        collected.Add(advisory);
                        if (vuln.Modified is { } mod && (maxModified is null || mod > maxModified))
                            maxModified = mod;
                    }
                }
            }

            if (skipped > 0)
                _logger.LogDebug("OSV detail fetch skipped {Skipped} cached vulns", skipped);

            string? nextCursor =
                maxModified?.ToString("O", CultureInfo.InvariantCulture) ?? state.Cursor;
            return (collected, FeedSyncResult.Ok(collected.Count, 0, nextCursor));
        }
        catch (OsvRateLimitedException ex)
        {
            _logger.LogInformation("OSV rate-limited; next sync at {RetryAt:u}", ex.RetryAt);
            return (Array.Empty<Advisory>(), FeedSyncResult.RateLimited(ex.RetryAt, state.Cursor));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OSV query failed");
            return (Array.Empty<Advisory>(), FeedSyncResult.Fail(ex.Message, state.Cursor));
        }
    }

    private async Task<IReadOnlyList<string>> PostBatchAsync(
        IEnumerable<OsvQuery> batch,
        CancellationToken ct
    )
    {
        List<OsvBatchQuery> body = new();
        List<OsvQuery> ordered = new();
        foreach (OsvQuery query in batch)
        {
            string? eco = OsvEcosystemMapper.ToOsv(query.Ecosystem);
            if (eco is null)
                continue;
            body.Add(new OsvBatchQuery(new OsvPackage(query.PackageName, eco), query.Version));
            ordered.Add(query);
        }

        if (body.Count == 0)
            return Array.Empty<string>();

        using HttpResponseMessage response = await _http
            .PostAsJsonAsync("v1/querybatch", new OsvBatchRequest(body), JsonOptions, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new OsvRateLimitedException(ParseRetryAfter(response));

        response.EnsureSuccessStatusCode();

        OsvBatchResponse? parsed = await response
            .Content.ReadFromJsonAsync<OsvBatchResponse>(JsonOptions, ct)
            .ConfigureAwait(false);

        if (parsed is null)
            return Array.Empty<string>();

        List<string> ids = new();
        for (int i = 0; i < parsed.Results.Count; i++)
        {
            IReadOnlyList<OsvVulnRef>? vulns = parsed.Results[i].Vulns;
            if (vulns is null)
                continue;
            foreach (OsvVulnRef reference in vulns)
                ids.Add(reference.Id);
        }
        return ids;
    }

    private async Task<OsvVulnerability?> GetVulnAsync(string id, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http
            .GetAsync($"v1/vulns/{Uri.EscapeDataString(id)}", ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new OsvRateLimitedException(ParseRetryAfter(response));

        if (!response.IsSuccessStatusCode)
            return null;
        return await response
            .Content.ReadFromJsonAsync<OsvVulnerability>(JsonOptions, ct)
            .ConfigureAwait(false);
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

    private static IEnumerable<Advisory> NormalizeVuln(OsvVulnerability vuln)
    {
        (Severity severity, double? cvss) = OsvSeverityMapper.Map(vuln);
        DateTime fetchedAt = DateTime.UtcNow;
        string referencesJson = JsonSerializer.Serialize(
            vuln.References?.Select(r => new { type = r.Type, url = r.Url }).ToArray()
                ?? Array.Empty<object>()
        );

        IReadOnlyList<OsvAffected> affected = vuln.Affected ?? Array.Empty<OsvAffected>();
        if (affected.Count == 0)
            yield break;

        foreach (OsvAffected entry in affected)
        {
            Ecosystem? ecosystem = OsvEcosystemMapper.FromOsv(entry.Package?.Ecosystem);
            if (ecosystem is null || string.IsNullOrWhiteSpace(entry.Package?.Name))
                continue;

            string rangesJson = JsonSerializer.Serialize(
                entry.Ranges ?? Array.Empty<OsvRange>(),
                PersistedJsonOptions
            );

            yield return new Advisory
            {
                Id = Guid.NewGuid(),
                Feed = Feed.Osv,
                ExternalId = vuln.Id,
                Ecosystem = ecosystem.Value,
                PackageName = entry.Package.Name,
                AffectedRangesJson = rangesJson,
                Severity = severity,
                Cvss = cvss,
                Summary = vuln.Summary ?? vuln.Details ?? string.Empty,
                ReferencesJson = referencesJson,
                PublishedAt = vuln.Published ?? fetchedAt,
                ModifiedAt = vuln.Modified ?? fetchedAt,
                FetchedAt = fetchedAt,
            };
        }
    }

    private static DateTime? TryParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        if (
            DateTime.TryParse(
                cursor,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed
            )
        )
            return parsed.ToUniversalTime();
        return null;
    }

    private static IEnumerable<IEnumerable<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (int start = 0; start < source.Count; start += size)
        {
            int end = Math.Min(start + size, source.Count);
            T[] slice = new T[end - start];
            for (int i = start; i < end; i++)
                slice[i - start] = source[i];
            yield return slice;
        }
    }
}
