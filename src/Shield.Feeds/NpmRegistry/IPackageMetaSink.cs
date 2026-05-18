using Shield.Core.Domain;

namespace Shield.Feeds.NpmRegistry;

public interface IPackageMetaSink
{
    ValueTask UpsertAsync(IReadOnlyList<PackageMeta> packages, CancellationToken ct);
}

public sealed class InMemoryPackageMetaSink : IPackageMetaSink
{
    private readonly List<PackageMeta> _packages = [];

    public IReadOnlyList<PackageMeta> Packages => _packages;

    public ValueTask UpsertAsync(IReadOnlyList<PackageMeta> packages, CancellationToken ct)
    {
        _packages.AddRange(packages);
        return ValueTask.CompletedTask;
    }
}

public interface IPackageNameSource
{
    ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken ct);
}

public sealed class InMemoryPackageNameSource : IPackageNameSource
{
    private readonly IReadOnlyList<string> _names;

    public InMemoryPackageNameSource(IEnumerable<string> names)
    {
        _names = names.ToArray();
    }

    public ValueTask<IReadOnlyList<string>> GetPackageNamesAsync(CancellationToken ct) =>
        ValueTask.FromResult(_names);
}
