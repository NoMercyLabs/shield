namespace Shield.Api.Services.Ecosystems;

// Index of every registered IEcosystem, resolved once per scope. Consumers call .For(ecosystem)
// instead of switching on Ecosystem values themselves.
public interface IEcosystemRegistry
{
    IEcosystem? For(Ecosystem ecosystem);
    IReadOnlyCollection<IEcosystem> All { get; }
}

public sealed class EcosystemRegistry : IEcosystemRegistry
{
    private readonly Dictionary<Ecosystem, IEcosystem> _byEcosystem;

    public EcosystemRegistry(IEnumerable<IEcosystem> ecosystems)
    {
        _byEcosystem = ecosystems.ToDictionary(eco => eco.Ecosystem);
    }

    public IEcosystem? For(Ecosystem ecosystem) =>
        _byEcosystem.TryGetValue(ecosystem, out IEcosystem? value) ? value : null;

    public IReadOnlyCollection<IEcosystem> All => _byEcosystem.Values;
}
