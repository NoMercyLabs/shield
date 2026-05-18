using Shield.Api.Services.ManifestEditors;
using Shield.Feeds.NpmRegistry;

namespace Shield.Api.Services.Ecosystems;

public sealed class NpmEcosystem : IEcosystem
{
    private readonly NpmPackageClient _registry;
    private readonly NpmManifestEditor _editor;

    public NpmEcosystem(NpmPackageClient registry, NpmManifestEditor editor)
    {
        _registry = registry;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Npm;
    public string DefaultManifestPath => "package.json";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => $"https://www.npmjs.com/package/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://www.npmjs.com/package/{packageName}/v/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        NpmPackageDocument? doc = await _registry.GetPackageAsync(packageName, ct);
        if (
            doc?.DistTags is null
            || !doc.DistTags.TryGetValue("latest", out string? tagged)
            || string.IsNullOrWhiteSpace(tagged)
        )
            return null;
        DateTime? published = null;
        if (doc.Time is not null && doc.Time.TryGetValue(tagged, out DateTime time))
            published = time;
        return new(tagged, published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);
}
