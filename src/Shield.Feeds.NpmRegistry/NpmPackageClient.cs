using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.NpmRegistry;

public sealed class NpmPackageClient
{
    private readonly HttpClient _http;

    public NpmPackageClient(HttpClient http)
    {
        _http = http;
    }

    public async ValueTask<NpmPackageDocument?> GetPackageAsync(
        string packageName,
        CancellationToken ct
    )
    {
        string encoded = EncodePackageName(packageName);
        HttpResponseMessage response = await _http.GetAsync(encoded, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NpmPackageDocument>(
            options: new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken: ct
        );
    }

    private static string EncodePackageName(string packageName)
    {
        if (packageName.StartsWith('@'))
        {
            int slash = packageName.IndexOf('/');
            if (slash > 0)
            {
                string scope = Uri.EscapeDataString(packageName[..slash]);
                string name = Uri.EscapeDataString(packageName[(slash + 1)..]);
                return $"{scope}/{name}";
            }
        }

        return Uri.EscapeDataString(packageName);
    }
}

public sealed class NpmPackageDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public Dictionary<string, DateTime>? Time { get; set; }

    [JsonPropertyName("maintainers")]
    public NpmMaintainer[]? Maintainers { get; set; }

    [JsonPropertyName("dist-tags")]
    public Dictionary<string, string>? DistTags { get; set; }

    [JsonPropertyName("versions")]
    public Dictionary<string, NpmVersion>? Versions { get; set; }
}

public sealed class NpmMaintainer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class NpmVersion
{
    [JsonPropertyName("dist")]
    public NpmDist? Dist { get; set; }

    [JsonPropertyName("deprecated")]
    public string? Deprecated { get; set; }
}

public sealed class NpmDist
{
    [JsonPropertyName("shasum")]
    public string? Shasum { get; set; }

    [JsonPropertyName("tarball")]
    public string? Tarball { get; set; }
}
