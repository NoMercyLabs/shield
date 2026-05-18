namespace Shield.Feeds.Ghsa;

public sealed class GhsaOptions
{
    public const string SectionName = "Shield:Feeds:Ghsa";

    public string? Pat { get; set; }
    public int PageSize { get; set; } = 100;
    public string Endpoint { get; set; } = "https://api.github.com/graphql";
    public string UserAgent { get; set; } = "shield-feeds-ghsa";
}
