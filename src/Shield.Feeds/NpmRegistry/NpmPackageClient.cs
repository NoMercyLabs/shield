using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shield.Feeds.NpmRegistry;

public sealed class NpmPackageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new NpmRegistryRateLimitedException(ParseRetryAfter(response));

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NpmPackageDocument>(
            options: JsonOptions,
            cancellationToken: ct
        );
    }

    // npm's downloads API lives on api.npmjs.org, not registry.npmjs.org. The HttpClient
    // configured for this client has BaseAddress = registry; passing an absolute URI
    // overrides it. Single-package calls only — scoped packages (e.g. @nestjs/core) can't
    // use the batched endpoint, and mixing breaks the response shape, so we keep it simple.
    public async ValueTask<long?> GetWeeklyDownloadsAsync(string packageName, CancellationToken ct)
    {
        string encoded = EncodePackageName(packageName);
        Uri url = new($"https://api.npmjs.org/downloads/point/last-week/{encoded}");

        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new NpmRegistryRateLimitedException(ParseRetryAfter(response));

        // 404 = npm has no stats for this package (very new, unpublished, or scoped+private).
        // 4xx body usually `{"error":"..."}`; treat any non-2xx as "unknown" rather than fatal.
        if (!response.IsSuccessStatusCode)
            return null;

        NpmDownloadsPoint? body = await response.Content.ReadFromJsonAsync<NpmDownloadsPoint>(
            options: JsonOptions,
            cancellationToken: ct
        );
        return body?.Downloads;
    }

    private static DateTimeOffset ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta.HasValue)
                return DateTimeOffset.UtcNow + header.Delta.Value;
            if (header.Date.HasValue)
                return header.Date.Value;
        }

        return DateTimeOffset.UtcNow.AddMinutes(5);
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

    // npm registry historically returns either a string (the deprecation message) OR a
    // boolean `false` for not-deprecated versions. System.Text.Json's default `string?`
    // converter blows up on the bool form — see GHSA-style payloads emitted by older
    // package versions in the registry. Tolerant converter maps bool/null → null,
    // string → string. Mapping code treats null/empty as "not deprecated".
    [JsonPropertyName("deprecated")]
    [JsonConverter(typeof(StringOrBoolToStringConverter))]
    public string? Deprecated { get; set; }
}

internal sealed class StringOrBoolToStringConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True
            or JsonTokenType.False
            or JsonTokenType.Null
            or JsonTokenType.Number => null,
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

public sealed class NpmDist
{
    [JsonPropertyName("shasum")]
    public string? Shasum { get; set; }

    [JsonPropertyName("tarball")]
    public string? Tarball { get; set; }
}

public sealed class NpmDownloadsPoint
{
    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("package")]
    public string? Package { get; set; }
}
