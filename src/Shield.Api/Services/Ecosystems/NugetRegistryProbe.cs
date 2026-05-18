using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shield.Api.Services.Ecosystems;

// Lightweight latest-stable-version probe for NuGet packages. Hits the v3 flat-container index
// for the version list, then the registration index for publish timestamps. Distinct from the
// advisory-driven NuGet sync (which doesn't exist yet) — this is solely for the Updates feature.
public interface INugetRegistryProbe
{
    Task<NugetLatestInfo?> GetLatestStableAsync(string packageName, CancellationToken ct);
}

public sealed record NugetLatestInfo(string Version, DateTime? PublishedAt);

public sealed class NugetRegistryProbe : INugetRegistryProbe
{
    private readonly HttpClient _http;

    public NugetRegistryProbe(HttpClient http)
    {
        _http = http;
    }

    public async Task<NugetLatestInfo?> GetLatestStableAsync(
        string packageName,
        CancellationToken ct
    )
    {
        string lower = packageName.ToLowerInvariant();
        string versionsUrl = $"v3-flatcontainer/{lower}/index.json";

        FlatContainerIndex? versions;
        try
        {
            HttpResponseMessage response = await _http.GetAsync(versionsUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            versions = await response.Content.ReadFromJsonAsync<FlatContainerIndex>(ct);
        }
        catch
        {
            return null;
        }

        if (versions?.Versions is null || versions.Versions.Count == 0)
            return null;

        string? latest = SemVerHelper.PickLatestStable(versions.Versions, raw => raw);
        if (latest is null)
            return null;

        // PublishedAt — best-effort via registration index. NuGet's registration v3 stores
        // catalog entries with `published` timestamps. Skip on any failure; the 48h gate
        // turns into a no-op when PublishedAt is null (safer to ship than to silently drop).
        DateTime? publishedAt = null;
        try
        {
            string registrationUrl =
                $"v3/registration5-semver1/{lower}/{latest.ToLowerInvariant()}.json";
            HttpResponseMessage registration = await _http.GetAsync(registrationUrl, ct);
            if (registration.IsSuccessStatusCode)
            {
                RegistrationLeaf? leaf =
                    await registration.Content.ReadFromJsonAsync<RegistrationLeaf>(ct);
                if (leaf?.CatalogEntry is not null)
                {
                    HttpResponseMessage catalog = await _http.GetAsync(leaf.CatalogEntry, ct);
                    if (catalog.IsSuccessStatusCode)
                    {
                        CatalogEntry? entry = await catalog.Content.ReadFromJsonAsync<CatalogEntry>(
                            ct
                        );
                        if (entry?.Published is not null && entry.Published.Value.Year > 1900)
                        {
                            publishedAt = entry.Published.Value.UtcDateTime;
                        }
                    }
                }
            }
        }
        catch
        {
            /* publish date is best-effort */
        }

        return new(latest, publishedAt);
    }

    private sealed class FlatContainerIndex
    {
        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }
    }

    private sealed class RegistrationLeaf
    {
        [JsonPropertyName("catalogEntry")]
        public string? CatalogEntry { get; set; }
    }

    private sealed class CatalogEntry
    {
        [JsonPropertyName("published")]
        public DateTimeOffset? Published { get; set; }
    }
}
