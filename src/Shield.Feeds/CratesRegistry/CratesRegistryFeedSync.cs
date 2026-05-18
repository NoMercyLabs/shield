using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.CratesRegistry;

public sealed class CratesRegistryFeedSync : IFeedSync
{
    private readonly CratesPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<CratesRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public CratesRegistryFeedSync(
        CratesPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<CratesRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.CratesRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalMeta = 0;
        try
        {
            foreach (string name in await _nameSource.GetPackageNamesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                CratesCrateResponse? doc = await _client.GetCrateAsync(name, ct);
                if (doc?.Versions is null || doc.Versions.Count == 0)
                    continue;
                CratesOwnersResponse? owners = null;
                try
                {
                    owners = await _client.GetOwnersAsync(name, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogDebug(ex, "crates owners probe failed for {Crate}", name);
                }
                List<string> ownerLogins =
                    owners
                        ?.Users?.Select(user => user.Login ?? string.Empty)
                        .Where(login => !string.IsNullOrEmpty(login))
                        .ToList()
                    ?? [];
                List<PackageMeta> packages = CratesPackageMapping
                    .Expand(name, doc.Versions, fetchedAt, doc.Crate?.RecentDownloads, ownerLogins)
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
            _logger?.LogError(ex, "crates.io registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }
}
