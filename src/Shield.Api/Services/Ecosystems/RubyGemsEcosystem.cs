using System.Net;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

public sealed class RubyGemsEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public RubyGemsEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.RubyGems;
    public string DefaultManifestPath => "Gemfile";
    public bool SupportsAutomaticPullRequests => false;

    public string PackageUrl(string packageName) => $"https://rubygems.org/gems/{packageName}";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://rubygems.org/gems/{packageName}/versions/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync($"api/v1/gems/{packageName}.json", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        GemSummary? summary = await response.Content.ReadFromJsonAsync<GemSummary>(ct);
        if (summary is null || string.IsNullOrEmpty(summary.Version))
            return null;
        DateTime? published = null;
        try
        {
            HttpResponseMessage versionsResp = await _http.GetAsync(
                $"api/v1/versions/{packageName}.json",
                ct
            );
            if (versionsResp.IsSuccessStatusCode)
            {
                List<GemVersion>? versions = await versionsResp.Content.ReadFromJsonAsync<
                    List<GemVersion>
                >(ct);
                published = versions?.FirstOrDefault(v => v.Number == summary.Version)?.CreatedAt;
            }
        }
        catch
        {
            /* best-effort */
        }
        return new(summary.Version, published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for RubyGems is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class GemSummary
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class GemVersion
    {
        [JsonPropertyName("number")]
        public string? Number { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }
    }
}
