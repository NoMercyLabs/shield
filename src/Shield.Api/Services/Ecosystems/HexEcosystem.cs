using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

public sealed class HexEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public HexEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.Hex;
    public string DefaultManifestPath => "mix.exs";
    public bool SupportsAutomaticPullRequests => false;

    public string PackageUrl(string packageName) => $"https://hex.pm/packages/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://hex.pm/packages/{packageName}/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync($"api/packages/{packageName}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        HexPackage? doc = await response.Content.ReadFromJsonAsync<HexPackage>(ct);
        HexRelease? latest = SemVerHelper.PickLatestStable(
            doc?.Releases ?? [],
            release => release.Version
        );
        if (latest is null || string.IsNullOrEmpty(latest.Version))
            return null;
        return new(latest.Version, latest.InsertedAt);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for Hex is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class HexPackage
    {
        [JsonPropertyName("releases")]
        public List<HexRelease>? Releases { get; set; }
    }

    private sealed class HexRelease
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("inserted_at")]
        public DateTime? InsertedAt { get; set; }
    }
}
