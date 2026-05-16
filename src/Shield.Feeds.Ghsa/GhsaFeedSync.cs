using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.Ghsa;

public sealed class GhsaFeedSync : IFeedSync
{
    private readonly GhsaGraphQLClient _client;
    private readonly IAdvisorySink _sink;
    private readonly GhsaOptions _options;
    private readonly ILogger<GhsaFeedSync>? _logger;
    private readonly TimeProvider _time;

    public GhsaFeedSync(
        GhsaGraphQLClient client,
        IAdvisorySink sink,
        IOptions<GhsaOptions> options,
        TimeProvider? time = null,
        ILogger<GhsaFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _options = options.Value;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.Ghsa;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime publishedSince = ParseCursor(state.Cursor);
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int pageSize = _options.PageSize;
        int totalAdvisories = 0;
        string? lastPublishedAtIso = state.Cursor;
        string? afterCursor = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                GhsaAdvisoryPage page = await _client.QueryAdvisoriesAsync(
                    publishedSince,
                    pageSize,
                    afterCursor,
                    ct
                );

                foreach (GhsaAdvisoryNode node in page.Nodes)
                {
                    List<Advisory> advisories = GhsaMapping.Expand(node, fetchedAt).ToList();
                    if (advisories.Count > 0)
                    {
                        await _sink.UpsertAsync(advisories, ct);
                        totalAdvisories += advisories.Count;
                    }

                    DateTime advisoryPublishedUtc = DateTime.SpecifyKind(
                        node.PublishedAt,
                        DateTimeKind.Utc
                    );
                    lastPublishedAtIso = advisoryPublishedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
                }

                if (!page.HasNextPage || page.EndCursor is null)
                {
                    break;
                }

                afterCursor = page.EndCursor;
            }

            state.Cursor = lastPublishedAtIso;
            return FeedSyncResult.Ok(totalAdvisories, 0, lastPublishedAtIso);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "GHSA sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }

    private static DateTime ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return DateTime.UtcNow.AddDays(-30);
        }

        if (
            DateTime.TryParse(
                cursor,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                    | System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTime parsed
            )
        )
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return DateTime.UtcNow.AddDays(-30);
    }
}
