using System.Collections.Concurrent;

namespace Shield.Api.Auth.External;

// Lookup by key for the controller. Registered providers come in through DI as
// IEnumerable<IExternalLoginProvider>; the registry just gives the controller an O(1)
// "is this key real" probe + the public listing for `GET providers`.
public interface IExternalLoginProviderRegistry
{
    IReadOnlyList<IExternalLoginProvider> All { get; }
    bool TryResolve(string key, out IExternalLoginProvider provider);
}

public sealed class ExternalLoginProviderRegistry : IExternalLoginProviderRegistry
{
    private readonly IReadOnlyList<IExternalLoginProvider> _all;
    private readonly ConcurrentDictionary<string, IExternalLoginProvider> _byKey;

    public ExternalLoginProviderRegistry(IEnumerable<IExternalLoginProvider> providers)
    {
        _all = providers.ToList();
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (IExternalLoginProvider provider in _all)
            _byKey.TryAdd(provider.Key, provider);
    }

    public IReadOnlyList<IExternalLoginProvider> All => _all;

    public bool TryResolve(string key, out IExternalLoginProvider provider)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            provider = null!;
            return false;
        }
        return _byKey.TryGetValue(key, out provider!);
    }
}
