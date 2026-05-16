namespace Shield.Feeds.NpmRegistry;

public sealed class NpmRegistryOptions
{
    public const string SectionName = "Shield:Feeds:NpmRegistry";

    public string Endpoint { get; set; } = "https://registry.npmjs.org";
    public string UserAgent { get; set; } = "shield-feeds-npm-registry";
    public int MaxRequestsPerSecond { get; set; } = 50;
}
