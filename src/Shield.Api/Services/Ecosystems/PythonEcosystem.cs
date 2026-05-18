using System.Net;
using System.Text.Json;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Domain;

namespace Shield.Api.Services.Ecosystems;

public sealed class PythonEcosystem : IEcosystem
{
    private readonly HttpClient _http;
    private readonly PythonManifestEditor _editor;

    public PythonEcosystem(HttpClient http, PythonManifestEditor editor)
    {
        _http = http;
        _editor = editor;
    }

    public Ecosystem Ecosystem => Ecosystem.Python;
    public string DefaultManifestPath => "pyproject.toml";
    public bool SupportsAutomaticPullRequests => true;
    public IReadOnlySet<string> PopularPackageNames { get; } = new HashSet<string>();

    public string PackageUrl(string packageName) => $"https://pypi.org/project/{packageName}/";

    public string ChangelogUrl(string packageName, string version) =>
        $"https://pypi.org/project/{packageName}/{version}/";

    public async Task<LatestPackageInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        HttpResponseMessage response = await _http.GetAsync($"pypi/{packageName}/json", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (
            !doc.RootElement.TryGetProperty("info", out JsonElement info)
            || !info.TryGetProperty("version", out JsonElement versionElement)
        )
            return null;
        string? version = versionElement.GetString();
        if (string.IsNullOrEmpty(version))
            return null;

        DateTime? published = null;
        if (
            doc.RootElement.TryGetProperty("releases", out JsonElement releases)
            && releases.TryGetProperty(version, out JsonElement releaseArray)
            && releaseArray.ValueKind == JsonValueKind.Array
            && releaseArray.GetArrayLength() > 0
            && releaseArray[0].TryGetProperty("upload_time_iso_8601", out JsonElement uploadTime)
            && uploadTime.GetString() is { } iso
            && DateTime.TryParse(iso, out DateTime parsed)
        )
            published = parsed.ToUniversalTime();
        return new(version, published);
    }

    public ManifestEditOutcome Apply(
        string rootPath,
        InventoryItem item,
        string suggestedVersion
    ) => _editor.Apply(rootPath, item, suggestedVersion);
}
