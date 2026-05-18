using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

// Maven Central. Coordinates arrive as "groupId:artifactId" — Gradle uses the same registry
// and the same coordinate convention, so GradleEcosystem delegates to this class for probes.
public sealed class MavenEcosystem : IEcosystem
{
    private readonly HttpClient _http;

    public MavenEcosystem(HttpClient http)
    {
        _http = http;
    }

    public Ecosystem Ecosystem => Ecosystem.Maven;
    public string DefaultManifestPath => "pom.xml";
    public bool SupportsAutomaticPullRequests => false;

    public string PackageUrl(string packageName)
    {
        int sep = packageName.IndexOf(':');
        return sep <= 0
            ? $"https://central.sonatype.com/search?q={Uri.EscapeDataString(packageName)}"
            : $"https://central.sonatype.com/artifact/{packageName[..sep]}/{packageName[(sep + 1)..]}";
    }

    public string ChangelogUrl(string packageName, string version)
    {
        int sep = packageName.IndexOf(':');
        return sep <= 0
            ? PackageUrl(packageName)
            : $"https://central.sonatype.com/artifact/{packageName[..sep]}/{packageName[(sep + 1)..]}/{version}";
    }

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        int sep = packageName.IndexOf(':');
        if (sep <= 0)
            return null;
        string group = packageName[..sep];
        string artifact = packageName[(sep + 1)..];
        string url =
            $"solrsearch/select?q=g:{Uri.EscapeDataString(group)}+AND+a:{Uri.EscapeDataString(artifact)}&core=gav&rows=1&wt=json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        MavenSearchResponse? doc = await response.Content.ReadFromJsonAsync<MavenSearchResponse>(
            ct
        );
        MavenDoc? best = doc?.Response?.Docs?.FirstOrDefault();
        if (best is null || string.IsNullOrEmpty(best.LatestVersion ?? best.Version))
            return null;
        string version = best.LatestVersion ?? best.Version!;
        DateTime? published =
            best.TimestampMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(best.TimestampMs).UtcDateTime
                : null;
        return new(version, published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) =>
        new(
            ChangedFiles: [],
            FollowUpCommand: null,
            UnsupportedReason: "Automatic apply for Maven is not yet implemented.",
            CleanedFiles: [],
            CleanedDirectories: []
        );

    private sealed class MavenSearchResponse
    {
        [JsonPropertyName("response")]
        public MavenResponseBody? Response { get; set; }
    }

    private sealed class MavenResponseBody
    {
        [JsonPropertyName("docs")]
        public List<MavenDoc>? Docs { get; set; }
    }

    private sealed class MavenDoc
    {
        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        [JsonPropertyName("v")]
        public string? Version { get; set; }

        [JsonPropertyName("timestamp")]
        public long TimestampMs { get; set; }
    }
}
