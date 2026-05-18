using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Domain;

namespace Shield.Api.Contracts;

// CreateSourceRequest accepts ConfigJson either as a JSON string ("{\"path\":\"...\"}")
// or a JSON object ({ "path": "..." }). The controller normalises to string on the way in
// so scanners keep their existing string-backed parse path.
public sealed record CreateSourceRequest(
    SourceType Type,
    [Required] string Name,
    JsonElement ConfigJson,
    TimeSpan? ScanInterval = null,
    bool Enabled = true
);

public sealed record UpdateSourceRequest(
    [Required] string Name,
    JsonElement ConfigJson,
    TimeSpan? ScanInterval = null,
    bool Enabled = true,
    int? MinPackageAgeHours = null
);

public sealed record DetectedRemoteDto(
    string Host,
    string Owner,
    string Repo,
    string RemoteUrl,
    string? Branch
);

public sealed record SourceResponse(
    int Id,
    SourceType Type,
    string Name,
    string ConfigJson,
    TimeSpan ScanInterval,
    DateTime? LastScannedAt,
    string? LastError,
    bool Enabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DetectedRemoteDto? DetectedRemote,
    DateTime? LastBulkApplyAt = null,
    AutoFixMode AutoFixMode = AutoFixMode.Off,
    bool IsProduction = false,
    DateTime? LastManualBulkApplyAt = null,
    DateTime? ManualCooldownUntil = null,
    int MinPackageAgeHours = 48
)
{
    public static SourceResponse From(Source source)
    {
        // ManualCooldownUntil is the wall-clock the SPA shows next to a disabled "Bulk apply"
        // button. Null when no cooldown is active. Mirrors the controller gate (24h fixed).
        DateTime? cooldownUntil =
            source.LastManualBulkApplyAt.HasValue
            && (DateTime.UtcNow - source.LastManualBulkApplyAt.Value) < TimeSpan.FromHours(24)
                ? source.LastManualBulkApplyAt.Value.AddHours(24)
                : null;
        return new(
            source.Id,
            source.Type,
            source.Name,
            source.ConfigJson,
            source.ScanInterval,
            source.LastScannedAt,
            source.LastError,
            source.Enabled,
            source.CreatedAt,
            source.UpdatedAt,
            ParseDetectedRemote(source.DetectedRemote),
            source.LastBulkApplyAt,
            source.AutoFixMode,
            source.IsProduction,
            source.LastManualBulkApplyAt,
            cooldownUntil,
            source.MinPackageAgeHours
        );
    }

    private static readonly JsonSerializerOptions DetectedRemoteOptions = new(
        JsonSerializerDefaults.Web
    );

    private static DetectedRemoteDto? ParseDetectedRemote(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DetectedRemoteDto>(json, DetectedRemoteOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record SourceDetailResponse(SourceResponse Source, SnapshotSummary? LatestSnapshot);
