using System.Globalization;
using System.Text.Json;
using Shield.Api.Services.Security.Snapshots;

namespace Shield.Api.Services.Security.Handlers;

// Restores Source row N to its pre-edit shape. Reads BeforeJson as SourceSnapshot, looks up
// the row by TargetId, applies the snapshot. Bumps UpdatedAt to now so the audit timeline
// shows a fresh write event distinct from the original. Fails when the row was deleted
// after the original audit landed — caller surfaces as 409 since restoring a vanished row
// would need a source.delete undo first.
public sealed class SourceUpdateUndoHandler : IAuditUndoHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ShieldDbContext _db;

    public SourceUpdateUndoHandler(ShieldDbContext db)
    {
        _db = db;
    }

    public string Action => "source.update";

    public async Task<AuditUndoResult> UndoAsync(AuditEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(entry.BeforeJson))
            return new(false, "No captured before-state to restore.");

        if (!int.TryParse(entry.TargetId, CultureInfo.InvariantCulture, out int id))
            return new(false, $"Audit TargetId '{entry.TargetId}' is not a valid source id.");

        SourceSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SourceSnapshot>(entry.BeforeJson, s_jsonOptions);
        }
        catch (JsonException ex)
        {
            return new(false, $"Could not parse before-state: {ex.Message}");
        }
        if (snapshot is null)
            return new(false, "Before-state deserialized to null.");

        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return new(false, $"Source #{id} no longer exists.");

        source.Name = snapshot.Name;
        source.ConfigJson = snapshot.ConfigJson;
        source.ScanInterval = snapshot.ScanInterval;
        source.Enabled = snapshot.Enabled;
        source.AutoFixMode = snapshot.AutoFixMode;
        source.IsProduction = snapshot.IsProduction;
        source.MinPackageAgeHours = snapshot.MinPackageAgeHours;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new(true, $"Restored Source #{id} ({snapshot.Name}) to its pre-edit state.");
    }
}
