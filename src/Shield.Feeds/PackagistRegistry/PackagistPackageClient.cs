using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.PackagistRegistry;

public sealed class PackagistPackageClient
{
    private readonly HttpClient _http;

    public PackagistPackageClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<PackagistPackageResponse?> GetPackageAsync(
        string vendorSlashName,
        CancellationToken ct
    )
    {
        // Composer package names always look like "vendor/name". Packagist serves them as
        // /packages/{vendor}/{name}.json — slash must NOT be URL-encoded.
        string url = vendorSlashName + ".json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PackagistPackageResponse>(
            cancellationToken: ct
        );
    }
}

public sealed class PackagistPackageResponse
{
    [JsonPropertyName("package")]
    public PackagistPackage? Package { get; set; }
}

public sealed class PackagistPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public PackagistDownloads? Downloads { get; set; }

    [JsonPropertyName("maintainers")]
    public List<PackagistMaintainer>? Maintainers { get; set; }

    [JsonPropertyName("versions")]
    public Dictionary<string, PackagistVersion>? Versions { get; set; }
}

public sealed class PackagistDownloads
{
    [JsonPropertyName("monthly")]
    public long? Monthly { get; set; }
}

public sealed class PackagistMaintainer
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class PackagistVersion
{
    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("abandoned")]
    public object? Abandoned { get; set; }
}
