using System.Globalization;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.NugetRegistry;

public sealed class NugetRegistryFeedSync : IFeedSync
{
    private readonly NugetPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly ILogger<NugetRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;

    public NugetRegistryFeedSync(
        NugetPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        TimeProvider? time = null,
        ILogger<NugetRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public Feed Feed => Feed.NugetRegistry;

    public async ValueTask<FeedSyncResult> SyncAsync(FeedSyncState state, CancellationToken ct)
    {
        DateTime fetchedAt = _time.GetUtcNow().UtcDateTime;
        int totalPackageMeta = 0;

        try
        {
            IReadOnlyList<string> packageNames = await _nameSource.GetPackageNamesAsync(ct);
            foreach (string packageName in packageNames)
            {
                ct.ThrowIfCancellationRequested();

                NugetRegistrationIndex? index = await _client.GetRegistrationAsync(packageName, ct);
                if (index?.Items is null || index.Items.Count == 0)
                    continue;

                List<NugetCatalogEntryRef> versions = await FlattenVersionsAsync(index, ct);
                if (versions.Count == 0)
                    continue;

                // Search hit is best-effort: search index can lag the registration index by
                // minutes-to-hours for new packages, so a null hit shouldn't block sync.
                NugetSearchHit? hit = await SafeAsync(
                    () => _client.GetSearchHitAsync(packageName, ct),
                    "search probe failed",
                    packageName
                );

                List<PackageMeta> packages = NugetPackageMapping
                    .Expand(packageName, versions, fetchedAt, hit?.TotalDownloads, hit?.Owners)
                    .ToList();
                if (packages.Count > 0)
                {
                    await _sink.UpsertAsync(packages, ct);
                    totalPackageMeta += packages.Count;
                }
            }

            string cursor = NugetEpochs.FormatCursor(fetchedAt);
            state.Cursor = cursor;
            return FeedSyncResult.Ok(0, totalPackageMeta, cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "NuGet registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }

    private async ValueTask<List<NugetCatalogEntryRef>> FlattenVersionsAsync(
        NugetRegistrationIndex index,
        CancellationToken ct
    )
    {
        List<NugetCatalogEntryRef> versions = [];
        if (index.Items is null)
            return versions;

        foreach (NugetRegistrationPage page in index.Items)
        {
            if (page.Items is { Count: > 0 } inline)
            {
                foreach (NugetRegistrationLeaf leaf in inline)
                    if (leaf.CatalogEntry is not null)
                        versions.Add(leaf.CatalogEntry);
                continue;
            }
            // Paged: the registration root only includes the @id link, the leaves live one
            // GET away. NuGet uses this for packages with many versions (Newtonsoft.Json
            // ships hundreds, so the index would be huge if inlined).
            if (string.IsNullOrEmpty(page.Id))
                continue;
            NugetRegistrationPage? expanded = await _client.GetRegistrationPageAsync(page.Id, ct);
            if (expanded?.Items is null)
                continue;
            foreach (NugetRegistrationLeaf leaf in expanded.Items)
                if (leaf.CatalogEntry is not null)
                    versions.Add(leaf.CatalogEntry);
        }

        return versions;
    }

    private async ValueTask<T?> SafeAsync<T>(
        Func<ValueTask<T?>> probe,
        string context,
        string subject
    )
        where T : class
    {
        try
        {
            return await probe();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "{Context}: {Subject}", context, subject);
            return null;
        }
    }
}
