using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

public sealed class NugetEcosystem : IEcosystem
{
    private readonly INugetRegistryProbe _registry;
    private readonly NugetManifestEditor _editor;

    public NugetEcosystem(INugetRegistryProbe registry, NugetManifestEditor editor)
    {
        _registry = registry;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Nuget;
    public string DefaultManifestPath => "Directory.Packages.props";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => $"https://www.nuget.org/packages/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://www.nuget.org/packages/{packageName}/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        NugetLatestInfo? info = await _registry.GetLatestStableAsync(packageName, ct);
        return info is null ? null : new(info.Version, info.PublishedAt);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);
}
