using System.Net;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

public sealed class GoEcosystem : IEcosystem
{
    private readonly HttpClient _http;
    private readonly GoManifestEditor _editor;

    public GoEcosystem(HttpClient http, GoManifestEditor editor)
    {
        _http = http;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Go;
    public string DefaultManifestPath => "go.mod";
    public bool SupportsAutomaticPullRequests => true;

    public string PackageUrl(string packageName) => $"https://pkg.go.dev/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://pkg.go.dev/{packageName}@{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync($"{packageName}/@latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        GoProxyResponse? doc = await response.Content.ReadFromJsonAsync<GoProxyResponse>(ct);
        if (doc is null || string.IsNullOrEmpty(doc.Version))
            return null;
        return new(doc.Version, doc.Time);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);

    private sealed class GoProxyResponse
    {
        [JsonPropertyName("Version")]
        public string? Version { get; set; }

        [JsonPropertyName("Time")]
        public DateTime? Time { get; set; }
    }
}
