using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = ShieldRoles.Admin)]
public sealed class AccessController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly RoleManager<ShieldRole> _roleManager;

    public AccessController(
        ShieldDbContext db,
        UserManager<ShieldUser> userManager,
        RoleManager<ShieldRole> roleManager
    )
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // -------------------- users --------------------

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<AccessUserDto>>> ListUsers(CancellationToken ct)
    {
        List<ShieldUser> users = await _userManager.Users.ToListAsync(ct);
        List<AccessUserDto> result = new(users.Count);
        foreach (ShieldUser user in users)
        {
            IList<string> roles = await _userManager.GetRolesAsync(user);
            result.Add(
                new AccessUserDto(
                    user.Id,
                    user.UserName ?? string.Empty,
                    user.Email,
                    roles.ToList(),
                    user.CreatedAt
                )
            );
        }
        return Ok(result);
    }

    // Atomic invite — create account + assign role + optional source grants in one call.
    // Shield has no email pipeline, so the response includes the cleartext credentials
    // exactly once. The admin is expected to relay them to the user out of band.
    [HttpPost("invite")]
    public async Task<ActionResult<InviteUserResponse>> Invite(
        [FromBody] InviteUserRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required." });

        string role = request.Role;
        if (
            role != ShieldRoles.Admin
            && role != ShieldRoles.Maintainer
            && role != ShieldRoles.Viewer
        )
            return BadRequest(new { error = "Role must be Admin, Maintainer, or Viewer." });

        if (!await _roleManager.RoleExistsAsync(role))
        {
            IdentityResult roleCreate = await _roleManager.CreateAsync(new ShieldRole(role));
            if (!roleCreate.Succeeded)
                return Problem(
                    title: "Failed to create role",
                    detail: string.Join(", ", roleCreate.Errors.Select(error => error.Description))
                );
        }

        ShieldUser user = new()
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };
        IdentityResult create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            return BadRequest(
                new
                {
                    error = string.Join(", ", create.Errors.Select(error => error.Description)),
                }
            );

        IdentityResult assign = await _userManager.AddToRoleAsync(user, role);
        if (!assign.Succeeded)
            return Problem(
                title: "Failed to assign role",
                detail: string.Join(", ", assign.Errors.Select(error => error.Description))
            );

        Guid? actor = TryGetActor();
        List<SourceAccess> created = new();
        if (request.Grants is { Count: > 0 })
        {
            DateTime now = DateTime.UtcNow;
            HashSet<int> validSourceIds = (
                await _db
                    .Sources.Where(source => request.Grants.Select(g => g.SourceId).Contains(source.Id))
                    .Select(source => source.Id)
                    .ToListAsync(ct)
            ).ToHashSet();
            foreach (InviteGrant grant in request.Grants)
            {
                if (!validSourceIds.Contains(grant.SourceId))
                    continue;
                SourceAccess access = new()
                {
                    SourceId = grant.SourceId,
                    UserId = user.Id,
                    GroupId = null,
                    Level = grant.Level,
                    GrantedAt = now,
                    GrantedBy = actor,
                };
                _db.SourceAccesses.Add(access);
                created.Add(access);
            }
            if (created.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        List<SourceGrantResponse> grantResponses = created
            .Select(access => new SourceGrantResponse(
                access.Id,
                access.SourceId,
                access.UserId,
                user.UserName,
                access.GroupId,
                GroupName: null,
                access.Level,
                access.GrantedAt,
                access.GrantedBy
            ))
            .ToList();

        string loginUrl =
            $"{Request.Scheme}://{Request.Host}/login?username={Uri.EscapeDataString(user.UserName ?? string.Empty)}";
        return Ok(new InviteUserResponse(user.Id, user.UserName!, role, grantResponses, loginUrl));
    }

    // -------------------- groups --------------------

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<SourceGroupResponse>>> ListGroups(
        CancellationToken ct
    )
    {
        List<SourceGroup> groups = await _db
            .SourceGroups.AsNoTracking()
            .OrderBy(group => group.Name)
            .ToListAsync(ct);
        if (groups.Count == 0)
            return Ok(Array.Empty<SourceGroupResponse>());

        HashSet<int> ids = groups.Select(group => group.Id).ToHashSet();
        List<GroupMembership> memberships = await _db
            .GroupMemberships.AsNoTracking()
            .Where(membership => ids.Contains(membership.GroupId))
            .ToListAsync(ct);

        HashSet<Guid> userIds = memberships.Select(membership => membership.UserId).ToHashSet();
        Dictionary<Guid, string> usernames = await _userManager
            .Users.Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.UserName ?? string.Empty, ct);

        List<SourceGroupResponse> response = groups
            .Select(group => new SourceGroupResponse(
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt,
                memberships
                    .Where(membership => membership.GroupId == group.Id)
                    .Select(membership => new GroupMemberDto(
                        membership.UserId,
                        usernames.TryGetValue(membership.UserId, out string? name) ? name : string.Empty,
                        membership.AddedAt
                    ))
                    .ToList()
            ))
            .ToList();
        return Ok(response);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<SourceGroupResponse>> CreateGroup(
        [FromBody] CreateGroupRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        SourceGroup group = new()
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
        };
        _db.SourceGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return Ok(
            new SourceGroupResponse(
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt,
                Array.Empty<GroupMemberDto>()
            )
        );
    }

    [HttpPut("groups/{id:int}")]
    public async Task<ActionResult<SourceGroupResponse>> UpdateGroup(
        int id,
        [FromBody] UpdateGroupRequest request,
        CancellationToken ct
    )
    {
        SourceGroup? group = await _db.SourceGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        group.Name = request.Name.Trim();
        group.Description = request.Description;
        await _db.SaveChangesAsync(ct);
        return Ok(
            new SourceGroupResponse(
                group.Id,
                group.Name,
                group.Description,
                group.CreatedAt,
                Array.Empty<GroupMemberDto>()
            )
        );
    }

    [HttpDelete("groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id, CancellationToken ct)
    {
        SourceGroup? group = await _db.SourceGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return NotFound();
        // Cascade memberships + grants by hand — SQLite + EF without configured cascades.
        List<GroupMembership> memberships = await _db
            .GroupMemberships.Where(membership => membership.GroupId == id)
            .ToListAsync(ct);
        _db.GroupMemberships.RemoveRange(memberships);
        List<SourceAccess> grants = await _db
            .SourceAccesses.Where(access => access.GroupId == id)
            .ToListAsync(ct);
        _db.SourceAccesses.RemoveRange(grants);
        _db.SourceGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("groups/{id:int}/members")]
    public async Task<ActionResult<AddGroupMemberResponse>> AddMember(
        int id,
        [FromBody] AddGroupMemberRequest request,
        CancellationToken ct
    )
    {
        SourceGroup? group = await _db.SourceGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null)
            return NotFound();

        ShieldUser? user = null;
        if (!string.IsNullOrWhiteSpace(request.Username))
            user = await _userManager.FindByNameAsync(request.Username);
        if (user is null && !string.IsNullOrWhiteSpace(request.Email))
            user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return BadRequest(new { error = "User not found by username or email." });

        bool already = await _db.GroupMemberships.AnyAsync(
            membership => membership.GroupId == id && membership.UserId == user.Id,
            ct
        );
        if (already)
            return BadRequest(new { error = "User is already a member of this group." });

        GroupMembership entry = new()
        {
            GroupId = id,
            UserId = user.Id,
            AddedAt = DateTime.UtcNow,
        };
        _db.GroupMemberships.Add(entry);
        await _db.SaveChangesAsync(ct);
        return Ok(
            new AddGroupMemberResponse(
                new GroupMemberDto(user.Id, user.UserName ?? string.Empty, entry.AddedAt)
            )
        );
    }

    [HttpDelete("groups/{id:int}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(int id, Guid userId, CancellationToken ct)
    {
        GroupMembership? entry = await _db.GroupMemberships.FirstOrDefaultAsync(
            membership => membership.GroupId == id && membership.UserId == userId,
            ct
        );
        if (entry is null)
            return NotFound();
        _db.GroupMemberships.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // -------------------- per-source grants --------------------

    [HttpGet("sources/{id:int}")]
    public async Task<ActionResult<SourceGrantsResponse>> ListSourceGrants(
        int id,
        CancellationToken ct
    )
    {
        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();

        List<SourceAccess> grants = await _db
            .SourceAccesses.AsNoTracking()
            .Where(access => access.SourceId == id)
            .ToListAsync(ct);
        return Ok(new SourceGrantsResponse(id, await EnrichAsync(grants, ct)));
    }

    [HttpPost("sources/{id:int}/grant")]
    public async Task<ActionResult<SourceGrantResponse>> Grant(
        int id,
        [FromBody] GrantSourceRequest request,
        CancellationToken ct
    )
    {
        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();

        bool hasUser = request.UserId.HasValue;
        bool hasGroup = request.GroupId.HasValue;
        if (hasUser == hasGroup)
            return BadRequest(
                new { error = "Exactly one of userId or groupId is required." }
            );

        if (hasUser)
        {
            ShieldUser? user = await _userManager.FindByIdAsync(request.UserId!.Value.ToString());
            if (user is null)
                return BadRequest(new { error = "User not found." });
        }
        else
        {
            bool groupExists = await _db.SourceGroups.AnyAsync(
                group => group.Id == request.GroupId!.Value,
                ct
            );
            if (!groupExists)
                return BadRequest(new { error = "Group not found." });
        }

        SourceAccess access = new()
        {
            SourceId = id,
            UserId = request.UserId,
            GroupId = request.GroupId,
            Level = request.Level,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = TryGetActor(),
        };
        _db.SourceAccesses.Add(access);
        await _db.SaveChangesAsync(ct);

        SourceGrantResponse response = (await EnrichAsync(new[] { access }, ct)).Single();
        return Ok(response);
    }

    [HttpDelete("sources/{id:int}/grant/{grantId:int}")]
    public async Task<IActionResult> Revoke(int id, int grantId, CancellationToken ct)
    {
        SourceAccess? access = await _db.SourceAccesses.FirstOrDefaultAsync(
            grant => grant.Id == grantId && grant.SourceId == id,
            ct
        );
        if (access is null)
            return NotFound();
        _db.SourceAccesses.Remove(access);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<List<SourceGrantResponse>> EnrichAsync(
        IReadOnlyList<SourceAccess> grants,
        CancellationToken ct
    )
    {
        if (grants.Count == 0)
            return new();

        HashSet<Guid> userIds = grants
            .Where(grant => grant.UserId.HasValue)
            .Select(grant => grant.UserId!.Value)
            .ToHashSet();
        HashSet<int> groupIds = grants
            .Where(grant => grant.GroupId.HasValue)
            .Select(grant => grant.GroupId!.Value)
            .ToHashSet();

        Dictionary<Guid, string> usernames = userIds.Count == 0
            ? new()
            : await _userManager
                .Users.Where(user => userIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.UserName ?? string.Empty, ct);
        Dictionary<int, string> groupNames = groupIds.Count == 0
            ? new()
            : await _db
                .SourceGroups.Where(group => groupIds.Contains(group.Id))
                .ToDictionaryAsync(group => group.Id, group => group.Name, ct);

        return grants
            .Select(grant => new SourceGrantResponse(
                grant.Id,
                grant.SourceId,
                grant.UserId,
                grant.UserId.HasValue && usernames.TryGetValue(grant.UserId.Value, out string? name)
                    ? name
                    : null,
                grant.GroupId,
                grant.GroupId.HasValue && groupNames.TryGetValue(grant.GroupId.Value, out string? gn)
                    ? gn
                    : null,
                grant.Level,
                grant.GrantedAt,
                grant.GrantedBy
            ))
            .ToList();
    }

    private Guid? TryGetActor()
    {
        string? raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out Guid id) ? id : null;
    }
}
