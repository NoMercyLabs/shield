using System.Net;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

public sealed class RustEcosystem : IEcosystem
{
    private readonly HttpClient _http;
    private readonly RustManifestEditor _editor;

    public RustEcosystem(HttpClient http, RustManifestEditor editor)
    {
        _http = http;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Rust;
    public string DefaultManifestPath => "Cargo.toml";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => $"https://crates.io/crates/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://crates.io/crates/{packageName}/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync(
            $"api/v1/crates/{packageName.ToLowerInvariant()}",
            ct
        );
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        CratesResponse? doc = await response.Content.ReadFromJsonAsync<CratesResponse>(ct);
        string? version = doc?.Crate?.MaxStableVersion ?? doc?.Crate?.MaxVersion;
        if (string.IsNullOrEmpty(version))
            return null;
        DateTime? published = doc!.Versions?.FirstOrDefault(v => v.Num == version)?.CreatedAt;
        return new(version, published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);

    private sealed class CratesResponse
    {
        [JsonPropertyName("crate")]
        public CrateInfo? Crate { get; set; }

        [JsonPropertyName("versions")]
        public List<CrateVersion>? Versions { get; set; }
    }

    private sealed class CrateInfo
    {
        [JsonPropertyName("max_stable_version")]
        public string? MaxStableVersion { get; set; }

        [JsonPropertyName("max_version")]
        public string? MaxVersion { get; set; }
    }

    private sealed class CrateVersion
    {
        [JsonPropertyName("num")]
        public string? Num { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}
