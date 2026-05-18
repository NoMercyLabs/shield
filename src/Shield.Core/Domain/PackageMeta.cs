namespace Shield.Core.Domain;

public class PackageMeta
{
    public Guid Id { get; set; }
    public Ecosystem Ecosystem { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public string MaintainersJson { get; set; } = "[]";
    public string? TarballSha { get; set; }
    public bool Deprecated { get; set; }

    // Weekly downloads from the package's home registry — the popularity signal that
    // replaces the curated KnownPopularPackages list. Null = registry has no per-package
    // count (Pub, Hex, SwiftPM) or sync hasn't reached this version yet. Stored as long
    // because npm packages like react / lodash routinely exceed int.MaxValue per week.
    public long? WeeklyDownloads { get; set; }
    public DateTime FetchedAt { get; set; }
}
