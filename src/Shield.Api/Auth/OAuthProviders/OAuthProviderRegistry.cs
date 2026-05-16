using Shield.Core.Domain;

namespace Shield.Api.Auth.OAuthProviders;

public interface IOAuthProviderRegistry
{
    IOAuthProvider Resolve(OAuthProvider provider);
    bool TryResolve(OAuthProvider provider, out IOAuthProvider adapter);
    IEnumerable<IOAuthProvider> All();
}

public sealed class OAuthProviderRegistry : IOAuthProviderRegistry
{
    private readonly Dictionary<OAuthProvider, IOAuthProvider> _providers;

    public OAuthProviderRegistry(IEnumerable<IOAuthProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Provider);
    }

    public IOAuthProvider Resolve(OAuthProvider provider) =>
        _providers.TryGetValue(provider, out IOAuthProvider? adapter)
            ? adapter
            : throw new InvalidOperationException($"No OAuth adapter for {provider}");

    public bool TryResolve(OAuthProvider provider, out IOAuthProvider adapter)
    {
        if (_providers.TryGetValue(provider, out IOAuthProvider? resolved))
        {
            adapter = resolved;
            return true;
        }
        adapter = null!;
        return false;
    }

    public IEnumerable<IOAuthProvider> All() => _providers.Values;
}
