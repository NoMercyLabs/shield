namespace Shield.Feeds.PackagistRegistry;

public sealed class PackagistRegistryOptions
{
    public const string SectionName = "Shield:Feeds:PackagistRegistry";
    public string Endpoint { get; set; } = "https://packagist.org/packages/";
    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";
}
