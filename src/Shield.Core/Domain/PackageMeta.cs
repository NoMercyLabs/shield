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
    public DateTime FetchedAt { get; set; }
}
