using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.RubyGemsRegistry;

public sealed class RubyGemsRegistryFeedSync : IFeedSync
{
    private readonly RubyGemsPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<RubyGemsRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public RubyGemsRegistryFeedSync(
        RubyGemsPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<RubyGemsRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.RubyGemsRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalMeta = 0;
        try
        {
            foreach (string name in await _nameSource.GetPackageNamesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                RubyGemsGem? gem = await _client.GetGemAsync(name, ct);
                if (gem is null)
                    continue;
                List<RubyGemsVersion>? versions = await _client.GetVersionsAsync(name, ct);
                if (versions is null || versions.Count == 0)
                    continue;
                List<PackageMeta> packages = RubyGemsPackageMapping
                    .Expand(name, gem, versions, fetchedAt)
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
            _logger?.LogError(ex, "RubyGems registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }
}
