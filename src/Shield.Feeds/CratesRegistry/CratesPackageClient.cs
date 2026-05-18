using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.CratesRegistry;

public sealed class CratesPackageClient
{
    private readonly HttpClient _http;

    public CratesPackageClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<CratesCrateResponse?> GetCrateAsync(
        string crateName,
        CancellationToken ct
    )
    {
        string url = "crates/" + Uri.EscapeDataString(crateName);
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<CratesCrateResponse>(cancellationToken: ct);
    }

    // Owners endpoint surfaces named maintainers — the crate response carries `recent_downloads`
    // (90-day) and `downloads` (total) but only ever flattens owners as opaque ids.
    public async ValueTask<CratesOwnersResponse?> GetOwnersAsync(
        string crateName,
        CancellationToken ct
    )
    {
        string url = "crates/" + Uri.EscapeDataString(crateName) + "/owners";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<CratesOwnersResponse>(
            cancellationToken: ct
        );
    }
}

public sealed class CratesCrateResponse
{
    [JsonPropertyName("crate")]
    public CratesCrate? Crate { get; set; }

    [JsonPropertyName("versions")]
    public List<CratesVersion>? Versions { get; set; }
}

public sealed class CratesCrate
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("recent_downloads")]
    public long? RecentDownloads { get; set; }

    [JsonPropertyName("downloads")]
    public long? Downloads { get; set; }
}

public sealed class CratesVersion
{
    [JsonPropertyName("num")]
    public string Num { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("yanked")]
    public bool Yanked { get; set; }
}

public sealed class CratesOwnersResponse
{
    [JsonPropertyName("users")]
    public List<CratesOwner>? Users { get; set; }
}

public sealed class CratesOwner
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}
