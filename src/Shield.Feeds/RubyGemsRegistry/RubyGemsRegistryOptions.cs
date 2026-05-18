namespace Shield.Feeds.RubyGemsRegistry;

public sealed class RubyGemsRegistryOptions
{
    public const string SectionName = "Shield:Feeds:RubyGemsRegistry";
    public string Endpoint { get; set; } = "https://rubygems.org/api/v1/";
    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";
}
