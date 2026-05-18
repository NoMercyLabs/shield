using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Feeds.NpmRegistry;

public sealed class NpmRegistryFeedSync : IFeedSync, IDisposable
{
    private readonly NpmPackageClient _client;
    private readonly IPackageMetaSink _sink;
    private readonly IPackageNameSource _nameSource;
    private readonly NpmRegistryOptions _options;
    private readonly ILogger<NpmRegistryFeedSync>? _logger;
    private readonly TimeProvider _time;
    private readonly TokenBucketLimiter _limiter;

    public NpmRegistryFeedSync(
        NpmPackageClient client,
        IPackageMetaSink sink,
        IPackageNameSource nameSource,
        IOptions<NpmRegistryOptions> options,
        TimeProvider? time = null,
        ILogger<NpmRegistryFeedSync>? logger = null
    )
    {
        _client = client;
        _sink = sink;
        _nameSource = nameSource;
        _options = options.Value;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _limiter = new TokenBucketLimiter(_options.MaxRequestsPerSecond, _time);
    }

    public Feed Feed => Feed.NpmRegistry;

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
                await _limiter.AcquireAsync(ct);

                NpmPackageDocument? document = await _client.GetPackageAsync(packageName, ct);
                if (document is null)
                {
                    continue;
                }

                List<PackageMeta> packages = NpmPackageMapping.Expand(document, fetchedAt).ToList();
                if (packages.Count > 0)
                {
                    await _sink.UpsertAsync(packages, ct);
                    totalPackageMeta += packages.Count;
                }
            }

            string cursor = fetchedAt.ToString("yyyy-MM-ddTHH:mm:ssZ");
            state.Cursor = cursor;
            return FeedSyncResult.Ok(0, totalPackageMeta, cursor);
        }
        catch (NpmRegistryRateLimitedException ex)
        {
            _logger?.LogInformation(
                "npm registry rate-limited; next sync at {RetryAt:u}",
                ex.RetryAt
            );
            return FeedSyncResult.RateLimited(ex.RetryAt, state.Cursor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "npm registry sync failed");
            return FeedSyncResult.Fail(ex.Message, state.Cursor);
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
