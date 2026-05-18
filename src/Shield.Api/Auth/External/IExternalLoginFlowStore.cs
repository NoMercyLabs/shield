using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Shield.Api.Auth.External;

// Short-lived mapping from a server-issued flowId to whatever upstream state the provider
// needs to poll (device_code, client_id, scopes, return path). Mirrors the GitHub
// device-flow store in Services/ but stays generic so a sibling Gitea/GitLab adapter can
// reuse it. Lives in MemoryCache (host-local, fine for single-host Shield; clustered
// deploys are out of scope until v0.2).
public interface IExternalLoginFlowStore
{
    string Issue(ExternalLoginFlowEntry entry);
    ExternalLoginFlowEntry? Find(string flowId);
    void Remove(string flowId);
}

public sealed record ExternalLoginFlowEntry(
    string ProviderKey,
    string DeviceCode,
    string ClientId,
    string Scopes,
    string ReturnPath,
    DateTime ExpiresAt
);

public sealed class ExternalLoginFlowStore : IExternalLoginFlowStore
{
    private const string CacheKeyPrefix = "external-login-flow::";
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public ExternalLoginFlowStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string Issue(ExternalLoginFlowEntry entry)
    {
        string flowId = GenerateFlowId();
        DateTimeOffset absoluteExpiry =
            entry.ExpiresAt > DateTime.UtcNow + EntryTtl
                ? new(entry.ExpiresAt, TimeSpan.Zero)
                : DateTimeOffset.UtcNow + EntryTtl;
        _cache.Set(CacheKeyPrefix + flowId, entry, absoluteExpiry);
        return flowId;
    }

    public ExternalLoginFlowEntry? Find(string flowId) =>
        _cache.TryGetValue(CacheKeyPrefix + flowId, out ExternalLoginFlowEntry? entry)
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
