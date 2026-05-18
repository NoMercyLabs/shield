using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.PackagistRegistry;

public sealed class PackagistRegistryFeedSync : IFeedSync
{
    private readonly PackagistPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<PackagistRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public PackagistRegistryFeedSync(
        PackagistPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<PackagistRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.PackagistRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalMeta = 0;
        try
        {
            foreach (string name in await _nameSource.GetPackageNamesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                // Composer package names without a vendor/ prefix can't be resolved against
                // packagist — skip them rather than 404 on every cycle.
                if (!name.Contains('/'))
                    continue;
                PackagistPackageResponse? doc = await _client.GetPackageAsync(name, ct);
                if (doc?.Package is null)
                    continue;
                List<PackageMeta> packages = PackagistPackageMapping
                    .Expand(doc.Package, fetchedAt)
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
            _logger?.LogError(ex, "Packagist registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }
}
