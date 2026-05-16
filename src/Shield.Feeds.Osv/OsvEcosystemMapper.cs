using Shield.Core.Domain;

namespace Shield.Feeds.Osv;

internal static class OsvEcosystemMapper
{
    public static string? ToOsv(Ecosystem ecosystem) =>
        ecosystem switch
        {
            Ecosystem.Npm => "npm",
            Ecosystem.Nuget => "NuGet",
            Ecosystem.Composer => "Packagist",
            Ecosystem.Gradle => "Maven",
            Ecosystem.Python => "PyPI",
            Ecosystem.Go => "Go",
            Ecosystem.Rust => "crates.io",
            Ecosystem.Os => null,
            _ => null,
        };

    public static Ecosystem? FromOsv(string? osvEcosystem)
    {
        if (string.IsNullOrWhiteSpace(osvEcosystem))
            return null;

        string normalized = osvEcosystem.Split(':')[0].Trim();
        return normalized.ToLowerInvariant() switch
        {
            "npm" => Ecosystem.Npm,
            "nuget" => Ecosystem.Nuget,
            "packagist" => Ecosystem.Composer,
            "maven" => Ecosystem.Gradle,
            "pypi" => Ecosystem.Python,
            "go" => Ecosystem.Go,
            "crates.io" => Ecosystem.Rust,
            _ => null,
        };
    }
}
