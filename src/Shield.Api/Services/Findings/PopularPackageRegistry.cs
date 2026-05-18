namespace Shield.Api.Services.Findings;

public sealed class PopularPackageRegistry : IPopularPackageRegistry
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    private readonly IReadOnlyDictionary<Ecosystem, IReadOnlySet<string>> _byEcosystem;

    public PopularPackageRegistry()
    {
        _byEcosystem = new Dictionary<Ecosystem, IReadOnlySet<string>>
        {
            [Ecosystem.Npm] = new HashSet<string>(
                KnownPopularPackages.Npm,
                StringComparer.OrdinalIgnoreCase
            ),
            [Ecosystem.Nuget] = new HashSet<string>(
                KnownPopularPackages.Nuget,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }

    public IReadOnlySet<string> For(Ecosystem ecosystem) =>
        _byEcosystem.TryGetValue(ecosystem, out IReadOnlySet<string>? set) ? set : EmptySet;
}
