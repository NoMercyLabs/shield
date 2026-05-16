using System.Text.Json.Serialization;

namespace Shield.Feeds.Osv.Models;

internal sealed record OsvBatchRequest([property: JsonPropertyName("queries")] IReadOnlyList<OsvBatchQuery> Queries);

internal sealed record OsvBatchQuery(
    [property: JsonPropertyName("package")] OsvPackage Package,
    [property: JsonPropertyName("version")] string Version
);

internal sealed record OsvPackage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ecosystem")] string Ecosystem
);

internal sealed record OsvBatchResponse([property: JsonPropertyName("results")] IReadOnlyList<OsvBatchResult> Results);

internal sealed record OsvBatchResult([property: JsonPropertyName("vulns")] IReadOnlyList<OsvVulnRef>? Vulns);

internal sealed record OsvVulnRef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("modified")] DateTime? Modified
);

internal sealed record OsvVulnerability(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("details")] string? Details,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string>? Aliases,
    [property: JsonPropertyName("modified")] DateTime? Modified,
    [property: JsonPropertyName("published")] DateTime? Published,
    [property: JsonPropertyName("affected")] IReadOnlyList<OsvAffected>? Affected,
    [property: JsonPropertyName("severity")] IReadOnlyList<OsvSeverity>? Severity,
    [property: JsonPropertyName("references")] IReadOnlyList<OsvReference>? References,
    [property: JsonPropertyName("database_specific")] OsvDatabaseSpecific? DatabaseSpecific
);

internal sealed record OsvAffected(
    [property: JsonPropertyName("package")] OsvPackage? Package,
    [property: JsonPropertyName("ranges")] IReadOnlyList<OsvRange>? Ranges,
    [property: JsonPropertyName("versions")] IReadOnlyList<string>? Versions
);

internal sealed record OsvRange(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("events")] IReadOnlyList<OsvRangeEvent>? Events
);

internal sealed record OsvRangeEvent(
    [property: JsonPropertyName("introduced")] string? Introduced,
    [property: JsonPropertyName("fixed")] string? Fixed,
    [property: JsonPropertyName("last_affected")] string? LastAffected
);

internal sealed record OsvSeverity(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("score")] string? Score
);

internal sealed record OsvReference(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("url")] string? Url
);

internal sealed record OsvDatabaseSpecific(
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("cwe_ids")] IReadOnlyList<string>? CweIds
);
