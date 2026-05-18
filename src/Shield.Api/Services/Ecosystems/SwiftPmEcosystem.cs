using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

// SwiftPM packages reference git URLs directly — no centralised registry. We probe GitHub
// releases for github.com-hosted packages, return null for other hosts (caller treats as
// "no update detected", which is correct: we have no data).
public sealed class SwiftPmEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public SwiftPmEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.SwiftPM;
    public string DefaultManifestPath => "Package.swift";
    public bool SupportsAutomaticPullRequests => false;
    public IReadOnlySet<string> PopularPackageNames { get; } = new HashSet<string>();

    public string PackageUrl(string packageName) => packageName;

    public string ChangelogUrl(string packageName, string version) =>
        packageName.TrimEnd('/') + $"/releases/tag/{version}";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        if (!packageName.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return null;
        int idx = packageName.IndexOf("github.com", StringComparison.OrdinalIgnoreCase);
        string remainder = packageName[(idx + "github.com".Length)..].TrimStart('/', ':');
        remainder = remainder.TrimEnd('/');
        if (remainder.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            remainder = remainder[..^4];
        string[] parts = remainder.Split('/');
        if (parts.Length < 2)
            return null;

        HttpResponseMessage response = await _http.GetAsync(
            $"repos/{parts[0]}/{parts[1]}/releases/latest",
            ct
        );
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        GitHubRelease? release = await response.Content.ReadFromJsonAsync<GitHubRelease>(ct);
        if (release is null || string.IsNullOrEmpty(release.TagName))
            return null;
        return new(release.TagName.TrimStart('v', 'V'), release.PublishedAt);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for SwiftPM is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }
    }
}
