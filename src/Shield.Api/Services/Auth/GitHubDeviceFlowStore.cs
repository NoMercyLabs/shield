using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Shield.Api.Services.Auth;

// Short-lived mapping from a server-issued flowId to GitHub's device_code. Keeps the
// device_code off the SPA so a stolen flowId can't be polled outside of an authenticated
// Shield session. TTL matches GitHub's device-code expiry (15 minutes is the documented
// upper bound for github.com).
public interface IGitHubDeviceFlowStore
{
    string Issue(GitHubDeviceFlowEntry entry);
    GitHubDeviceFlowEntry? Find(string flowId);
    void Remove(string flowId);
}

public sealed record GitHubDeviceFlowEntry(
    string DeviceCode,
    string ClientId,
    string Scopes,
    DateTime ExpiresAt
);

public sealed class GitHubDeviceFlowStore : IGitHubDeviceFlowStore
{
    private const string CacheKeyPrefix = "github-device-flow::";
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public GitHubDeviceFlowStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string Issue(GitHubDeviceFlowEntry entry)
    {
        string flowId = GenerateFlowId();
        // Cache TTL tracks the longer of "now + TTL" and the entry's own expires-at so a
        // GitHub-side expiry shorter than EntryTtl still controls the floor.
        DateTimeOffset absoluteExpiry =
            entry.ExpiresAt > DateTime.UtcNow + EntryTtl
                ? new(entry.ExpiresAt, TimeSpan.Zero)
                : DateTimeOffset.UtcNow + EntryTtl;
        _cache.Set(CacheKeyPrefix + flowId, entry, absoluteExpiry);
        return flowId;
    }

    public GitHubDeviceFlowEntry? Find(string flowId) =>
        _cache.TryGetValue(CacheKeyPrefix + flowId, out GitHubDeviceFlowEntry? entry)
            ? entry
            : null;

    public void Remove(string flowId) => _cache.Remove(CacheKeyPrefix + flowId);

    private static string GenerateFlowId()
    {
        Span<byte> buffer = stackalloc byte[24];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
