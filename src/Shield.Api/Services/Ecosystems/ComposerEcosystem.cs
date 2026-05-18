using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

public sealed class ComposerEcosystem : IEcosystem
{
    private readonly HttpClient _http;
    private readonly ComposerManifestEditor _editor;

    public ComposerEcosystem(HttpClient http, ComposerManifestEditor editor)
    {
        _http = http;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Composer;
    public string DefaultManifestPath => "composer.json";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => $"https://packagist.org/packages/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://packagist.org/packages/{packageName}#{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"p2/{packageName.ToLowerInvariant()}.json",
            ct
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        PackagistResponse? doc = await response.Content.ReadFromJsonAsync<PackagistResponse>(ct);
        if (
            doc?.Packages is null
            || !doc.Packages.TryGetValue(packageName, out List<PackagistVersion>? versions)
        )
            return null;

        PackagistVersion? best = SemVerHelper.PickLatestStable(versions, entry => entry.Version);
        return best is null ? null : new(best.Version!, best.Time);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);

    private sealed class PackagistResponse
    {
        [JsonPropertyName("packages")]
        public Dictionary<string, List<PackagistVersion>>? Packages { get; set; }
    }

    private sealed class PackagistVersion
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("time")]
        public DateTime? Time { get; set; }
    }
}
