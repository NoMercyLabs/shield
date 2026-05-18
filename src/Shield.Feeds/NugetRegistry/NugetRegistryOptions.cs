namespace Shield.Feeds.NugetRegistry;

public sealed class NugetRegistryOptions
{
    public const string SectionName = "Shield:Feeds:NugetRegistry";

    public string RegistrationEndpoint { get; set; } =
        "https://api.nuget.org/v3/registration5-semver1/";

    // Search endpoint exposes totalDownloads + owners + tags per package; the v3 registration
    // index does not. Two-call sync, but both are unauthenticated and rate-friendly.
    public string SearchEndpoint { get; set; } = "https://azuresearch-usnc.nuget.org/query";

    public string UserAgent { get; set; } = "Shield/1.0 (+https://shield.nomercy.tv)";

    public int MaxRequestsPerSecond { get; set; } = 10;
}
