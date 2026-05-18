using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/saved-filters")]
[Authorize]
public sealed class SavedFiltersController : ControllerBase
{
    private const int MaxQueryJsonLength = 8000;
    private const int MaxFiltersPerUser = 50;

    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;

    public SavedFiltersController(ShieldDbContext db, UserManager<ShieldUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedFilterResponse>>> List(
        [FromQuery] string kind = "findings",
        CancellationToken ct = default
    )
    {
        Guid userId = await ResolveUserIdAsync();
        List<SavedFilter> rows = await _db
            .SavedFilters.Where(filter => filter.UserId == userId && filter.Kind == kind)
            .OrderByDescending(filter => filter.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows.Select(SavedFilterResponse.From).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SavedFilterResponse>> Create(
        [FromBody] CreateSavedFilterRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name is required" });
        if (string.IsNullOrWhiteSpace(request.Kind))
            return BadRequest(new { error = "kind is required" });
        if (request.QueryJson is null)
            return BadRequest(new { error = "queryJson is required" });
        if (request.QueryJson.Length > MaxQueryJsonLength)
            return BadRequest(new { error = "queryJson too large" });

        Guid userId = await ResolveUserIdAsync();
        string trimmedName = request.Name.Trim();
        string trimmedKind = request.Kind.Trim();

        int existingCount = await _db.SavedFilters.CountAsync(
            filter => filter.UserId == userId,
            ct
        );
        if (existingCount >= MaxFiltersPerUser)
            return BadRequest(
                new { error = $"limit of {MaxFiltersPerUser} saved filters reached" }
            );

        // Same-name-same-kind overwrites — operators expect "Save current as..." with a
        // reused name to update the existing entry rather than create a dupe.
        SavedFilter? existing = await _db.SavedFilters.FirstOrDefaultAsync(
            filter =>
                filter.UserId == userId && filter.Kind == trimmedKind && filter.Name == trimmedName,
            ct
        );
        if (existing is not null)
        {
            existing.QueryJson = request.QueryJson;
            existing.CreatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(SavedFilterResponse.From(existing));
        }

        SavedFilter row = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = trimmedName,
            Kind = trimmedKind,
            QueryJson = request.QueryJson,
            CreatedAt = DateTime.UtcNow,
        };
        _db.SavedFilters.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(SavedFilterResponse.From(row));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        SavedFilter? row = await _db.SavedFilters.FirstOrDefaultAsync(
            filter => filter.Id == id && filter.UserId == userId,
            ct
        );
        if (row is null)
            return NotFound();
        _db.SavedFilters.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Guid> ResolveUserIdAsync()
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out Guid parsed))
            return parsed;
        ShieldUser? user = await _userManager.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }
}
