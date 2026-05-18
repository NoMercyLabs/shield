namespace Shield.Feeds.PyPiRegistry;

public sealed class PyPiRegistryOptions
{
    public const string SectionName = "Shield:Feeds:PyPiRegistry";
    public string PypiEndpoint { get; set; } = "https://pypi.org/pypi/";
    public string PypiStatsEndpoint { get; set; } = "https://pypistats.org/api/packages/";
    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";
}
