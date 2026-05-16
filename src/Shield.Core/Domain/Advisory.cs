namespace Shield.Core.Domain;

public class Advisory
{
    public Guid Id { get; set; }
    public Feed Feed { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public Ecosystem Ecosystem { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public string AffectedRangesJson { get; set; } = "[]";
    public Severity Severity { get; set; }
    public double? Cvss { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ReferencesJson { get; set; } = "[]";
    public DateTime PublishedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public DateTime FetchedAt { get; set; }
}
