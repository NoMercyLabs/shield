using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.HexRegistry;

public sealed class HexRegistryFeedSync : IFeedSync
{
    private readonly HexPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<HexRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public HexRegistryFeedSync(
        HexPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<HexRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.HexRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalMeta = 0;
        try
        {
            foreach (string name in await _nameSource.GetPackageNamesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                HexPackage? doc = await _client.GetPackageAsync(name, ct);
                if (doc is null)
                    continue;
                List<PackageMeta> packages = HexPackageMapping.Expand(doc, fetchedAt).ToList();
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
            _logger?.LogError(ex, "Hex registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }
}
