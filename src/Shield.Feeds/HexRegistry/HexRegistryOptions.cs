namespace Shield.Feeds.HexRegistry;

public sealed class HexRegistryOptions
{
    public const string SectionName = "Shield:Feeds:HexRegistry";
    public string Endpoint { get; set; } = "https://hex.pm/api/";
    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";
}
