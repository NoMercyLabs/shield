using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shield.Feeds.PyPiRegistry;

// Two upstreams: pypi.org for metadata, pypistats.org for download counts. PyPI doesn't
// expose any download endpoint of its own (deprecated in 2020); pypistats is the closest
// community-maintained substitute with stable JSON output and no auth.
public sealed class PyPiPackageClient
{
    private readonly HttpClient _http;
    private readonly PyPiRegistryOptions _options;

    public PyPiPackageClient(HttpClient http, IOptions<PyPiRegistryOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async ValueTask<PyPiPackageDocument?> GetPackageAsync(
        string packageName,
        CancellationToken ct
    )
    {
        string url = _options.PypiEndpoint.TrimEnd('/') + "/" + packageName + "/json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<PyPiPackageDocument>(cancellationToken: ct);
    }

    public async ValueTask<long?> GetWeeklyDownloadsAsync(string packageName, CancellationToken ct)
    {
        string url = _options.PypiStatsEndpoint.TrimEnd('/') + "/" + packageName + "/recent";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        PyPiStatsResponse? body = await response.Content.ReadFromJsonAsync<PyPiStatsResponse>(
            cancellationToken: ct
        );
        return body?.Data?.LastWeek;
    }
}

public sealed class PyPiPackageDocument
{
    [JsonPropertyName("info")]
    public PyPiInfo? Info { get; set; }

    [JsonPropertyName("releases")]
    public Dictionary<string, List<PyPiRelease>>? Releases { get; set; }
}

public sealed class PyPiInfo
{
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("maintainer")]
    public string? Maintainer { get; set; }

    [JsonPropertyName("yanked")]
    public bool Yanked { get; set; }
}

public sealed class PyPiRelease
{
    [JsonPropertyName("upload_time_iso_8601")]
    public DateTimeOffset? UploadTime { get; set; }

    [JsonPropertyName("yanked")]
    public bool Yanked { get; set; }
}

public sealed class PyPiStatsResponse
{
    [JsonPropertyName("data")]
    public PyPiStatsData? Data { get; set; }
}

public sealed class PyPiStatsData
{
    [JsonPropertyName("last_week")]
    public long? LastWeek { get; set; }
}
