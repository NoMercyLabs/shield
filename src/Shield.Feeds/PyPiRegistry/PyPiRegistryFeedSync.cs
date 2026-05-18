using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.PyPiRegistry;

public sealed class PyPiRegistryFeedSync : IFeedSync
{
    private readonly PyPiPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<PyPiRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public PyPiRegistryFeedSync(
        PyPiPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<PyPiRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.PyPiRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalMeta = 0;
        try
        {
            foreach (string name in await _nameSource.GetPackageNamesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                PyPiPackageDocument? doc = await _client.GetPackageAsync(name, ct);
                if (doc is null)
                    continue;
                long? downloads = null;
                try
                {
                    downloads = await _client.GetWeeklyDownloadsAsync(name, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogDebug(ex, "pypistats probe failed for {Package}", name);
                }
                List<PackageMeta> packages = PyPiPackageMapping
                    .Expand(name, doc, fetchedAt, downloads)
                    .ToList();
                if (packages.Count > 0)
                {
                    await _sink.UpsertAsync(packages, ct);
                    totalMeta += packages.Count;
                }
            }
            string cursor = fetchedAt.ToString(
                "yyyy-MM-ddTHH:mm:ssZ",
                CultureInfo.InvariantCulture
            );
            state.Cursor = cursor;
            return FeedSyncResult.Ok(0, totalMeta, cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "PyPI registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }
}
