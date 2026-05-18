using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

public sealed class PubEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public PubEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.Pub;
    public string DefaultManifestPath => "pubspec.yaml";
    public bool SupportsAutomaticPullRequests => false;

    public string PackageUrl(string packageName) => $"https://pub.dev/packages/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://pub.dev/packages/{packageName}/versions/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync($"api/packages/{packageName}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        PubResponse? doc = await response.Content.ReadFromJsonAsync<PubResponse>(ct);
        if (doc?.Latest is null || string.IsNullOrEmpty(doc.Latest.Version))
            return null;
        return new(doc.Latest.Version, doc.Latest.Published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for pub.dev is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class PubResponse
    {
        [JsonPropertyName("latest")]
        public PubVersion? Latest { get; set; }
    }

    private sealed class PubVersion
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("published")]
        public DateTime? Published { get; set; }
    }
}
