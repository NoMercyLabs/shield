using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services;

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

    public async Task RecordAsync(
        string action,
        string targetType,
        string targetId,
        object? details = null,
        CancellationToken ct = default
    )
    {
        HttpContext? ctx = _httpContextAccessor.HttpContext;
        ClaimsPrincipal? user = ctx?.User;
        Guid? actorId = null;
        string actorName = "system";
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
        }

        string? remoteIp = ctx?.Connection.RemoteIpAddress?.ToString();

        AuditEntry entry = new()
        {
            Id = Guid.NewGuid(),
            At = DateTime.UtcNow,
            ActorUserId = actorId,
            ActorName = actorName,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, s_jsonOptions),
            RemoteIp = remoteIp,
        };

        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
