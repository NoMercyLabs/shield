using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

public sealed class VcpkgEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public VcpkgEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.Vcpkg;
    public string DefaultManifestPath => "vcpkg.json";
    public bool SupportsAutomaticPullRequests => false;

    public string PackageUrl(string packageName) =>
        $"https://github.com/microsoft/vcpkg/tree/master/ports/{packageName}";

    public string ChangelogUrl(string packageName, string version) => PackageUrl(packageName);

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"microsoft/vcpkg/master/ports/{packageName}/vcpkg.json",
            ct
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        VcpkgManifest? doc = await response.Content.ReadFromJsonAsync<VcpkgManifest>(ct);
        string? version =
            doc?.Version ?? doc?.VersionSemver ?? doc?.VersionDate ?? doc?.VersionString;
        if (string.IsNullOrEmpty(version))
            return null;
        return new(version, null);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for vcpkg is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class VcpkgManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("version-semver")]
        public string? VersionSemver { get; set; }

        [JsonPropertyName("version-date")]
        public string? VersionDate { get; set; }

        [JsonPropertyName("version-string")]
        public string? VersionString { get; set; }
    }
}
