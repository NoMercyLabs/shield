using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.RubyGemsRegistry;

public sealed class RubyGemsPackageClient
{
    private readonly HttpClient _http;

    public RubyGemsPackageClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<RubyGemsGem?> GetGemAsync(string gemName, CancellationToken ct)
    {
        string url = "gems/" + Uri.EscapeDataString(gemName) + ".json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<RubyGemsGem>(cancellationToken: ct);
    }

    public async ValueTask<List<RubyGemsVersion>?> GetVersionsAsync(
        string gemName,
        CancellationToken ct
    )
    {
        string url = "versions/" + Uri.EscapeDataString(gemName) + ".json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<List<RubyGemsVersion>>(
            cancellationToken: ct
        );
    }
}

public sealed class RubyGemsGem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("authors")]
    public string? Authors { get; set; }
}

public sealed class RubyGemsVersion
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("authors")]
    public string? Authors { get; set; }

    [JsonPropertyName("yanked")]
    public bool Yanked { get; set; }
}
