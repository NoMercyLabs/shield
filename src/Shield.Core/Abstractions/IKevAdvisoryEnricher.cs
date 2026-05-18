namespace Shield.Core.Abstractions;

public sealed record KevCatalogEntry(
    string CveId,
    DateTime DateAdded,
    DateTime? DueDate,
    string? VendorProject,
    string? Product,
    string? VulnerabilityName,
    string? ShortDescription
);

public sealed record KevEnrichmentResult(int Updated, int Inserted);

public interface IKevAdvisoryEnricher
{
    ValueTask<KevEnrichmentResult> ApplyAsync(
        IReadOnlyList<KevCatalogEntry> entries,
        CancellationToken ct
    );
}
