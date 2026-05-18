using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.HexRegistry;

public sealed class HexPackageClient
{
    private readonly HttpClient _http;

    public HexPackageClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<HexPackage?> GetPackageAsync(string packageName, CancellationToken ct)
    {
        string url = "packages/" + Uri.EscapeDataString(packageName);
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<HexPackage>(cancellationToken: ct);
    }
}

public sealed class HexPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public HexDownloads? Downloads { get; set; }

    [JsonPropertyName("meta")]
    public HexMeta? Meta { get; set; }

    [JsonPropertyName("releases")]
    public List<HexRelease>? Releases { get; set; }
}

public sealed class HexDownloads
{
    [JsonPropertyName("week")]
    public long? Week { get; set; }
}

public sealed class HexMeta
{
    [JsonPropertyName("maintainers")]
    public List<string>? Maintainers { get; set; }
}

public sealed class HexRelease
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("inserted_at")]
    public DateTimeOffset? InsertedAt { get; set; }
}
