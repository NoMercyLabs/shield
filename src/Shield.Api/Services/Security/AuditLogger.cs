using System.Security.Claims;
using System.Text.Json;

namespace Shield.Api.Services.Security;

public sealed class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ShieldDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogger(ShieldDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<Guid> RecordAsync(
        string action,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken ct = default
    ) =>
        WriteAsync(
            action,
            targetType,
            targetId,
            before: null,
            after: null,
            details,
            isReversible: false,
            ct
        );

    public async Task<Guid> RecordWriteAsync(
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after,
        object? details = null,
        CancellationToken ct = default
    )
    {
        // IsReversible flips on when we have a beforeJson to roll back to. afterJson without
        // beforeJson is informational-only (e.g. create with no prior shape — the inverse is
        // a delete, which the handler reads from TargetId).
        bool isReversible =
            before is not null
            || string.Equals(action, "source.create", StringComparison.Ordinal)
            || string.Equals(action, "channel.create", StringComparison.Ordinal)
            || string.Equals(action, "user.role.changed", StringComparison.Ordinal);
        return await WriteAsync(
            action,
            targetType,
            targetId,
            before,
            after,
            details,
            isReversible,
            ct
        );
    }

    private async Task<Guid> WriteAsync(
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after,
        object? details,
        bool isReversible,
        CancellationToken ct
    )
    {
        HttpContext? ctx = _httpContextAccessor.HttpContext;
        ClaimsPrincipal? user = ctx?.User;
        Guid? actorId = null;
        string actorName = "system";
        string? impersonatedBy = null;
        if (user?.Identity is { IsAuthenticated: true })
        {
            string? rawId =
                user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            if (Guid.TryParse(rawId, out Guid parsed))
                actorId = parsed;
            actorName =
                user.Identity.Name
                ?? user.FindFirstValue(ClaimTypes.Name)
                ?? user.FindFirstValue("preferred_username")
                ?? "anonymous";
            impersonatedBy = user.FindFirstValue(
                RequireOriginalIdentityAttribute.ImpersonatorClaimType
            );
        }

        string? remoteIp = ctx?.Connection.RemoteIpAddress?.ToString();

        // Re-shape details to carry impersonatedBy alongside whatever the caller passed in,
        // so downstream audit log readers see the originating admin without parsing
        // separate rows. Caller-provided null becomes {impersonatedBy:...}; anything else
        // is wrapped as {…original…, impersonatedBy:...}.
        object? finalDetails = details;
        if (impersonatedBy is not null)
        {
            Dictionary<string, object?> wrapped = new();
            if (details is not null)
            {
                string raw = JsonSerializer.Serialize(details, s_jsonOptions);
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                            wrapped[prop.Name] = JsonSerializer.Deserialize<object?>(
                                prop.Value.GetRawText()
                            );
                    }
                    else
                    {
                        wrapped["value"] = JsonSerializer.Deserialize<object?>(raw);
                    }
                }
                catch
                {
                    wrapped["value"] = raw;
                }
            }
            wrapped["impersonatedBy"] = impersonatedBy;
            finalDetails = wrapped;
        }

        AuditEntry entry = new()
        {
            Id = Guid.NewGuid(),
            At = DateTime.UtcNow,
            ActorUserId = actorId,
            ActorName = actorName,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = finalDetails is null
                ? null
                : JsonSerializer.Serialize(finalDetails, s_jsonOptions),
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, s_jsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, s_jsonOptions),
            IsReversible = isReversible,
            RemoteIp = remoteIp,
        };

        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry.Id;
    }
}
