using Shield.Core.Domain;

namespace Shield.Core.Options;

public sealed class FeedOptions
{
    public const string SectionName = "Shield:Feeds";

    public Dictionary<Feed, FeedSettings> Feeds { get; set; } = new();
}

public sealed class FeedSettings
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Cadence { get; set; } = TimeSpan.FromMinutes(15);
    public string? ApiKey { get; set; }
    public string? BaseUrl { get; set; }
}
