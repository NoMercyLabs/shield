namespace Shield.Feeds.CratesRegistry;

public sealed class CratesRegistryOptions
{
    public const string SectionName = "Shield:Feeds:CratesRegistry";
    public string Endpoint { get; set; } = "https://crates.io/api/v1/";
    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";
}
