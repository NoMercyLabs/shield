using System.Text.Json.Serialization;

namespace Shield.Feeds.Kev.Models;

internal sealed class KevCatalogDocument
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("catalogVersion")]
    public string? CatalogVersion { get; set; }

    [JsonPropertyName("dateReleased")]
    public DateTime? DateReleased { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("vulnerabilities")]
    public List<KevCatalogEntryDto> Vulnerabilities { get; set; } = new();
}

internal sealed class KevCatalogEntryDto
{
    [JsonPropertyName("cveID")]
    public string CveId { get; set; } = string.Empty;

    [JsonPropertyName("vendorProject")]
    public string? VendorProject { get; set; }

    [JsonPropertyName("product")]
    public string? Product { get; set; }

    [JsonPropertyName("vulnerabilityName")]
    public string? VulnerabilityName { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("requiredAction")]
    public string? RequiredAction { get; set; }

    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; set; }

    [JsonPropertyName("knownRansomwareCampaignUse")]
    public string? KnownRansomwareCampaignUse { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}
