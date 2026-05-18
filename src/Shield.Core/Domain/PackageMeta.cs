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

    // Weekly downloads from the package's home registry — popularity signal the anomaly
    // detector uses to gate typosquat scoring (popular candidates skip typosquat, suspect
    // candidates are required to be low-traffic). Null = registry has no per-package count
    // or sync hasn't reached this version yet. Stored as long because npm packages like
    // react / lodash routinely exceed int.MaxValue per week.
    public long? WeeklyDownloads { get; set; }
    public DateTime FetchedAt { get; set; }
}
