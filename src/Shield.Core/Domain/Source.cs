namespace Shield.Core.Domain;

public class Source
{
    public int Id { get; set; }
    public SourceType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public TimeSpan ScanInterval { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public string? LastError { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
