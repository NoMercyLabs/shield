using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

// Gradle uses Maven Central as the primary registry. Probe delegates to MavenEcosystem; the
// editor patches build.gradle / build.gradle.kts which is a different format than pom.xml.
public sealed class GradleEcosystem : IEcosystem
{
    private readonly MavenEcosystem _maven;
    private readonly GradleManifestEditor _editor;

    public GradleEcosystem(MavenEcosystem maven, GradleManifestEditor editor)
    {
        _maven = maven;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Gradle;
    public string DefaultManifestPath => "build.gradle";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => _maven.PackageUrl(packageName);

    public string ChangelogUrl(string packageName, string version) =>
        _maven.ChangelogUrl(packageName, version);

    public Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    ) => _maven.GetLatestStableAsync(packageName, ct);

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);
}
