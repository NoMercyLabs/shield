using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shield.Feeds.NugetRegistry;

// Two-endpoint client. The v3 registration index has versions + per-version catalog leaves
// (publish dates, deprecation flags, dependencies); the search service supplies totalDownloads
// and owners. NuGet doesn't expose maintainer email lists publicly, so "Owners" is the closest
// thing we have to the maintainer-churn signal npm registry gives us.
public sealed class NugetPackageClient
{
    private readonly HttpClient _http;
    private readonly NugetRegistryOptions _options;

    public NugetPackageClient(HttpClient http, IOptions<NugetRegistryOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async ValueTask<NugetRegistrationIndex?> GetRegistrationAsync(
        string packageName,
        CancellationToken ct
    )
    {
        string url =
            _options.RegistrationEndpoint.TrimEnd('/')
            + "/"
            + packageName.ToLowerInvariant()
            + "/index.json";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<NugetRegistrationIndex>(
            cancellationToken: ct
        );
    }

    // The search API returns one match per package id; we always set take=1 and match on
    // packageid: prefix to avoid drift from fuzzy ranking.
    public async ValueTask<NugetSearchHit?> GetSearchHitAsync(
        string packageName,
        CancellationToken ct
    )
    {
        string url =
            _options.SearchEndpoint
            + "?q=packageid:"
            + Uri.EscapeDataString(packageName)
            + "&prerelease=false&take=1&semVerLevel=2.0.0";
        HttpResponseMessage response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        NugetSearchResponse? body = await response.Content.ReadFromJsonAsync<NugetSearchResponse>(
            cancellationToken: ct
        );
        return body?.Data?.FirstOrDefault();
    }

    // Catalog leaves carry the per-version published timestamp; the registration index can
    // ship them inline or as a remote link, depending on package size. Walk both shapes.
    public async ValueTask<NugetCatalogEntry?> GetCatalogEntryAsync(
        string catalogUrl,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(catalogUrl))
            return null;
        HttpResponseMessage response = await _http.GetAsync(catalogUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<NugetCatalogEntry>(cancellationToken: ct);
    }

    // Paged registration walker. High-version-count packages (Newtonsoft.Json, EF Core, etc.)
    // return the index with `@id` links instead of inlined leaves; this fetches one page.
    public async ValueTask<NugetRegistrationPage?> GetRegistrationPageAsync(
        string pageUrl,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(pageUrl))
            return null;
        HttpResponseMessage response = await _http.GetAsync(pageUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<NugetRegistrationPage>(
            cancellationToken: ct
        );
    }
}

public sealed class NugetRegistrationIndex
{
    [JsonPropertyName("items")]
    public List<NugetRegistrationPage>? Items { get; set; }
}

public sealed class NugetRegistrationPage
{
    [JsonPropertyName("items")]
    public List<NugetRegistrationLeaf>? Items { get; set; }

    // Paged registrations only set the @id on the page; the items array is null until we
    // GET the @id explicitly. Caller has to follow the link for big packages.
    [JsonPropertyName("@id")]
    public string? Id { get; set; }
}

public sealed class NugetRegistrationLeaf
{
    [JsonPropertyName("catalogEntry")]
    public NugetCatalogEntryRef? CatalogEntry { get; set; }
}

public sealed class NugetCatalogEntryRef
{
    [JsonPropertyName("@id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("published")]
    public DateTimeOffset? Published { get; set; }

    [JsonPropertyName("deprecation")]
    public NugetDeprecation? Deprecation { get; set; }
}

public sealed class NugetDeprecation
{
    [JsonPropertyName("reasons")]
    public List<string>? Reasons { get; set; }
}

public sealed class NugetCatalogEntry
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("published")]
    public DateTimeOffset? Published { get; set; }
}

public sealed class NugetSearchResponse
{
    [JsonPropertyName("data")]
    public List<NugetSearchHit>? Data { get; set; }
}

public sealed class NugetSearchHit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("totalDownloads")]
    public long? TotalDownloads { get; set; }

    [JsonPropertyName("owners")]
    public List<string>? Owners { get; set; }
}

internal static class NugetEpochs
{
    // Sentinel: NuGet's registration uses 1900-01-01 to mean "deleted/unlisted" — published
    // dates older than ~2003 are noise and must be discarded before they pollute the
    // PublishedAt signal the anomaly detector keys off.
    private static readonly DateTime UsableFloor = new(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime? Normalize(DateTimeOffset? raw)
    {
        if (raw is null)
            return null;
        DateTime utc = raw.Value.UtcDateTime;
        return utc < UsableFloor ? null : utc;
    }

    public static string FormatCursor(DateTime utc) =>
        utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
