using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class AuditController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    private readonly ShieldDbContext _db;

    public AuditController(ShieldDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Authorize(Roles = ShieldRoles.Admin)]
    public async Task<ActionResult<AuditPage>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? action = null,
        [FromQuery] string? targetType = null,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        IQueryable<AuditEntry> query = _db.AuditEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(entry => entry.Action == action);
        if (!string.IsNullOrWhiteSpace(targetType))
            query = query.Where(entry => entry.TargetType == targetType);

        int total = await query.CountAsync(ct);
        List<AuditEntry> rows = await query
            .OrderByDescending(entry => entry.At)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        List<AuditEntryResponse> items = rows.Select(entry => new AuditEntryResponse(
                entry.Id,
                entry.At,
                entry.ActorUserId,
                entry.ActorName,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.DetailsJson,
                entry.RemoteIp
            ))
            .ToList();

        return Ok(new AuditPage(items, total, page, pageSize));
    }
}
