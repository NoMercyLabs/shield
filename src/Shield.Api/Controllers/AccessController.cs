using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = ShieldPolicies.Admin)]
[NoApiToken]
public sealed class AccessController : ControllerBase
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromDays(7);

    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly RoleManager<ShieldRole> _roleManager;
    private readonly IInviteEmailSender _emailSender;
    private readonly IAuditLogger _audit;

    public AccessController(
        ShieldDbContext db,
        UserManager<ShieldUser> userManager,
        RoleManager<ShieldRole> roleManager,
        IInviteEmailSender emailSender,
        IAuditLogger audit
    )
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailSender = emailSender;
        _audit = audit;
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
                new(
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

    // Token-based invite — creates a pending Invite row + emails the invitee a link. The
    // ShieldUser is NOT created here; the user materialises only when the invitee accepts via
    // /api/auth/accept-invite. Pre-creating empty rows would leak email-existence info to
    // anyone holding the token (see CLAUDE.md threat-model notes).
    [HttpPost("invite")]
    [RequireOriginalIdentity]
    public async Task<ActionResult<InviteUserResponse>> Invite(
        [FromBody] InviteUserRequest request,
        CancellationToken ct
    )
    {
        // Two-mode invite: either a free-form email (legacy) OR a pre-bound external identity
        // picked from the GitHub orgs/members/search picker. Exactly one must be set.
        bool hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        InviteExternalIdentity? external = request.ExternalIdentity;
        bool hasExternal =
            external is not null
            && !string.IsNullOrWhiteSpace(external.Provider)
            && !string.IsNullOrWhiteSpace(external.SubjectId)
            && !string.IsNullOrWhiteSpace(external.Login);
        if (!hasEmail && !hasExternal)
            return BadRequest(new { error = "Either email or externalIdentity is required." });

        // If the picker passed a public email along with the external identity, use that for
        // display + the optional courtesy notification. Otherwise the email column carries a
        // synthetic placeholder so the existing display surfaces (audit row, pending list)
        // still have something legible without leaking that the field is "empty".
        string displayEmail;
        if (hasEmail)
        {
            displayEmail = request.Email!.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(external!.Email))
        {
            displayEmail = external.Email!.Trim();
        }
        else
        {
            displayEmail = $"{external.Login}@users.noreply.github.com";
        }

        string role = request.Role;
        if (
            role != ShieldRoles.Admin
            && role != ShieldRoles.Maintainer
            && role != ShieldRoles.Viewer
        )
            return BadRequest(new { error = "Role must be Admin, Maintainer, or Viewer." });

        IReadOnlyList<int> requestedGroupIds = request.SourceGroupIds ?? [];
        List<SourceGroup> groups =
            requestedGroupIds.Count == 0
                ? []
                : await _db
                    .SourceGroups.Where(group => requestedGroupIds.Contains(group.Id))
                    .ToListAsync(ct);
        if (groups.Count != requestedGroupIds.Distinct().Count())
            return BadRequest(new { error = "One or more source groups do not exist." });

        if (!await _roleManager.RoleExistsAsync(role))
        {
            IdentityResult roleCreate = await _roleManager.CreateAsync(new(role));
            if (!roleCreate.Succeeded)
                return Problem(
                    title: "Failed to create role",
                    detail: string.Join(", ", roleCreate.Errors.Select(error => error.Description))
                );
        }

        Guid? actor = TryGetActor();
        DateTime now = DateTime.UtcNow;
        Invite invite = new()
        {
            Id = Guid.NewGuid(),
            Email = displayEmail,
            Role = role,
            SourceGroupIdsCsv = string.Join(',', groups.Select(group => group.Id)),
            Token = GenerateInviteToken(),
            CreatedAt = now,
            ExpiresAt = now + InviteTtl,
            CreatedBy = actor,
            LastSentAt = now,
            PreBoundProvider = hasExternal ? NormalizeProvider(external!.Provider) : null,
            PreBoundSubjectId = hasExternal ? external!.SubjectId : null,
            PreBoundLogin = hasExternal ? external!.Login : null,
            PreBoundEmail = hasExternal ? external!.Email : null,
        };
        _db.Invites.Add(invite);
        await _db.SaveChangesAsync(ct);

        string inviterLogin = await ResolveInviterLoginAsync(actor, ct);
        string acceptUrl = BuildAcceptUrl(invite.Token);
        InviteEmailResult emailResult = await _emailSender.SendAsync(
            invite,
            acceptUrl,
            inviterLogin,
            groups.Select(group => group.Name).ToList(),
            ct
        );

        await _audit.RecordAsync(
            "access.invite.send",
            "Invite",
            invite.Id.ToString(),
            new
            {
                role,
                groupIds = groups.Select(group => group.Id).ToArray(),
                emailDelivered = emailResult.Sent,
                emailSkipReason = emailResult.Reason,
                preBoundProvider = invite.PreBoundProvider,
                preBoundLogin = invite.PreBoundLogin,
            },
            ct
        );

        return Ok(
            new InviteUserResponse(
                invite.Id,
                invite.Email,
                invite.Role,
                groups.Select(group => group.Id).ToList(),
                invite.ExpiresAt,
                acceptUrl,
                emailResult.Sent,
                emailResult.Reason,
                BuildPreBound(invite)
            )
        );
    }

    private static string NormalizeProvider(string raw) => raw.Trim().ToLowerInvariant();

    private static InvitePreBoundIdentity? BuildPreBound(Invite invite)
    {
        if (
            string.IsNullOrEmpty(invite.PreBoundProvider)
            || string.IsNullOrEmpty(invite.PreBoundSubjectId)
            || string.IsNullOrEmpty(invite.PreBoundLogin)
        )
            return null;
        return new(invite.PreBoundProvider, invite.PreBoundSubjectId, invite.PreBoundLogin);
    }

    // List pending invites (not accepted, not revoked, not expired). Admins use this to find
    // outstanding links they can resend or revoke. Email is fine to surface here since the
    // caller is already Admin-authenticated.
    [HttpGet("invites")]
    public async Task<ActionResult<IReadOnlyList<PendingInviteResponse>>> ListPending(
        CancellationToken ct
    )
    {
        DateTime now = DateTime.UtcNow;
        List<Invite> invites = await _db
            .Invites.AsNoTracking()
            .Where(invite =>
                invite.AcceptedAt == null && invite.RevokedAt == null && invite.ExpiresAt > now
            )
            .OrderByDescending(invite => invite.CreatedAt)
            .ToListAsync(ct);

        HashSet<int> groupIds = invites
            .SelectMany(invite => ParseGroupIds(invite.SourceGroupIdsCsv))
            .ToHashSet();
        Dictionary<int, string> groupNames =
            groupIds.Count == 0
                ? new()
                : await _db
                    .SourceGroups.Where(group => groupIds.Contains(group.Id))
                    .ToDictionaryAsync(group => group.Id, group => group.Name, ct);

        HashSet<Guid> creatorIds = invites
            .Where(invite => invite.CreatedBy.HasValue)
            .Select(invite => invite.CreatedBy!.Value)
            .ToHashSet();
        Dictionary<Guid, string> creatorLogins =
            creatorIds.Count == 0
                ? new()
                : await _userManager
                    .Users.Where(user => creatorIds.Contains(user.Id))
                    .ToDictionaryAsync(user => user.Id, user => user.UserName ?? string.Empty, ct);

        List<PendingInviteResponse> response = invites
            .Select(invite =>
            {
                IReadOnlyList<int> ids = ParseGroupIds(invite.SourceGroupIdsCsv);
                return new PendingInviteResponse(
                    invite.Id,
                    invite.Email,
                    invite.Role,
                    ids,
                    ids.Select(id => groupNames.TryGetValue(id, out string? name) ? name : $"#{id}")
                        .ToList(),
                    invite.CreatedAt,
                    invite.ExpiresAt,
                    invite.LastSentAt,
                    invite.ResendCount,
                    invite.CreatedBy.HasValue
                    && creatorLogins.TryGetValue(invite.CreatedBy.Value, out string? login)
                        ? login
                        : null,
                    BuildPreBound(invite),
                    invite.Token
                );
            })
            .ToList();
        return Ok(response);
    }

    [HttpPost("invite/{id:guid}/resend")]
    [RequireOriginalIdentity]
    public async Task<ActionResult<InviteUserResponse>> Resend(Guid id, CancellationToken ct)
    {
        Invite? invite = await _db.Invites.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (invite is null)
            return NotFound();
        if (invite.AcceptedAt is not null)
            return BadRequest(new { error = "Invite has already been accepted." });
        if (invite.RevokedAt is not null)
            return BadRequest(new { error = "Invite has been revoked." });
        if (invite.ExpiresAt <= DateTime.UtcNow)
        {
            // Refresh the expiry on a resend rather than 410'ing — a stale link is exactly the
            // case where the admin reaches for "resend".
            invite.ExpiresAt = DateTime.UtcNow + InviteTtl;
        }

        invite.LastSentAt = DateTime.UtcNow;
        invite.ResendCount += 1;
        await _db.SaveChangesAsync(ct);

        List<SourceGroup> groups = await ResolveGroupsAsync(invite.SourceGroupIdsCsv, ct);
        string inviterLogin = await ResolveInviterLoginAsync(invite.CreatedBy, ct);
        string acceptUrl = BuildAcceptUrl(invite.Token);
        InviteEmailResult emailResult = await _emailSender.SendAsync(
            invite,
            acceptUrl,
            inviterLogin,
            groups.Select(group => group.Name).ToList(),
            ct
        );

        await _audit.RecordAsync(
            "access.invite.resend",
            "Invite",
            invite.Id.ToString(),
            new { emailDelivered = emailResult.Sent, emailSkipReason = emailResult.Reason },
            ct
        );

        return Ok(
            new InviteUserResponse(
                invite.Id,
                invite.Email,
                invite.Role,
                groups.Select(group => group.Id).ToList(),
                invite.ExpiresAt,
                acceptUrl,
                emailResult.Sent,
                emailResult.Reason,
                BuildPreBound(invite)
            )
        );
    }

    [HttpDelete("invite/{id:guid}")]
    [RequireOriginalIdentity]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        Invite? invite = await _db.Invites.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (invite is null)
            return NotFound();
        if (invite.AcceptedAt is not null)
            return BadRequest(new { error = "Invite has already been accepted." });
        if (invite.RevokedAt is not null)
            return NoContent();

        invite.RevokedAt = DateTime.UtcNow;
        invite.RevokedBy = TryGetActor();
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "access.invite.revoke",
            "Invite",
            invite.Id.ToString(),
            details: null,
            ct
        );
        return NoContent();
    }

    // Public preview so the accept page can render context BEFORE the user signs in. The
    // response deliberately omits the email + creator id; only the role + group names + the
    // inviter's display login are surfaced.
    [HttpGet("invite/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicInvitePreview>> GetByToken(
        string token,
        CancellationToken ct
    )
    {
        Invite? invite = await _db
            .Invites.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Token == token, ct);
        if (invite is null)
            return NotFound();
        if (invite.AcceptedAt is not null || invite.RevokedAt is not null)
            return StatusCode(StatusCodes.Status410Gone);
        if (invite.ExpiresAt <= DateTime.UtcNow)
            return StatusCode(StatusCodes.Status410Gone);

        IReadOnlyList<int> ids = ParseGroupIds(invite.SourceGroupIdsCsv);
        List<string> names =
            ids.Count == 0
                ? []
                : await _db
                    .SourceGroups.Where(group => ids.Contains(group.Id))
                    .OrderBy(group => group.Name)
                    .Select(group => group.Name)
                    .ToListAsync(ct);

        string inviterLogin = await ResolveInviterLoginAsync(invite.CreatedBy, ct);
        return Ok(new PublicInvitePreview(invite.Role, names, inviterLogin, invite.ExpiresAt));
    }

    private async Task<List<SourceGroup>> ResolveGroupsAsync(string csv, CancellationToken ct)
    {
        IReadOnlyList<int> ids = ParseGroupIds(csv);
        if (ids.Count == 0)
            return [];
        return await _db.SourceGroups.Where(group => ids.Contains(group.Id)).ToListAsync(ct);
    }

    private async Task<string> ResolveInviterLoginAsync(Guid? actorId, CancellationToken ct)
    {
        if (!actorId.HasValue)
            return "an administrator";
        ShieldUser? user = await _userManager.FindByIdAsync(actorId.Value.ToString());
        if (user is null)
            return "an administrator";
        // Prefer the bound code-host login (matches what's shown on the inviter's GitHub
        // profile) over the local Shield UserName — invitees recognise their teammate's
        // public handle more reliably than the internal account name.
        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        UserLoginInfo? github = logins.FirstOrDefault(login =>
            string.Equals(login.LoginProvider, "Github", StringComparison.OrdinalIgnoreCase)
        );
        if (github is not null && !string.IsNullOrEmpty(github.ProviderDisplayName))
            return github.ProviderDisplayName;
        return user.UserName ?? user.Email ?? "an administrator";
    }

    private string BuildAcceptUrl(string token)
    {
        // Prefer the configured cookie domain when present (production deploys behind Caddy /
        // Cloudflare set this so emails land with the public URL); fall back to the inbound
        // request when running on localhost.
        string scheme = Request.Scheme;
        string host = Request.Host.Value ?? "localhost";
        return $"{scheme}://{host}/accept-invite?token={Uri.EscapeDataString(token)}";
    }

    private static IReadOnlyList<int> ParseGroupIds(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        List<int> ids = [];
        foreach (
            string part in csv.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (int.TryParse(part, out int id))
                ids.Add(id);
        }
        return ids;
    }

    private static string GenerateInviteToken()
    {
        // 32 random bytes → 256 bits of entropy → ~43 base64url chars. Token only travels
        // over HTTPS and is stored UNIQUE-indexed, so the random space is the entire security
        // boundary (no per-invite secret pepper). The /api/access/invite/{token} lookup is
        // O(1) via the index.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
                        usernames.TryGetValue(membership.UserId, out string? name)
                            ? name
                            : string.Empty,
                        membership.AddedAt
                    ))
                    .ToList()
            ))
            .ToList();
        return Ok(response);
    }

    [HttpPost("groups")]
    [RequireOriginalIdentity]
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
            new SourceGroupResponse(group.Id, group.Name, group.Description, group.CreatedAt, [])
        );
    }

    [HttpPut("groups/{id:int}")]
    [RequireOriginalIdentity]
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
            new SourceGroupResponse(group.Id, group.Name, group.Description, group.CreatedAt, [])
        );
    }

    [HttpDelete("groups/{id:int}")]
    [RequireOriginalIdentity]
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
    [RequireOriginalIdentity]
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
            new AddGroupMemberResponse(new(user.Id, user.UserName ?? string.Empty, entry.AddedAt))
        );
    }

    [HttpDelete("groups/{id:int}/members/{userId:guid}")]
    [RequireOriginalIdentity]
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
    [RequireOriginalIdentity]
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
            return BadRequest(new { error = "Exactly one of userId or groupId is required." });

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

        SourceGrantResponse response = (await EnrichAsync([access], ct)).Single();
        return Ok(response);
    }

    [HttpDelete("sources/{id:int}/grant/{grantId:int}")]
    [RequireOriginalIdentity]
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
            return [];

        HashSet<Guid> userIds = grants
            .Where(grant => grant.UserId.HasValue)
            .Select(grant => grant.UserId!.Value)
            .ToHashSet();
        HashSet<int> groupIds = grants
            .Where(grant => grant.GroupId.HasValue)
            .Select(grant => grant.GroupId!.Value)
            .ToHashSet();

        Dictionary<Guid, string> usernames =
            userIds.Count == 0
                ? new()
                : await _userManager
                    .Users.Where(user => userIds.Contains(user.Id))
                    .ToDictionaryAsync(user => user.Id, user => user.UserName ?? string.Empty, ct);
        Dictionary<int, string> groupNames =
            groupIds.Count == 0
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
                grant.GroupId.HasValue
                && groupNames.TryGetValue(grant.GroupId.Value, out string? gn)
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

// Lives outside AccessController so the class-level Admin policy on AccessController doesn't
// prevent a self-refresh by a non-admin Maintainer. Same /api/access/ prefix so the SPA's
// access.ts query helpers can keep `/access/...` paths consistent.
[ApiController]
[Route("api/access")]
[Authorize]
[NoApiToken]
public sealed class AccessGithubRefreshController : ControllerBase
{
    private readonly IGithubAccessResolver _githubAccess;
    private readonly IAuditLogger _audit;

    public AccessGithubRefreshController(IGithubAccessResolver githubAccess, IAuditLogger audit)
    {
        _githubAccess = githubAccess;
        _audit = audit;
    }

    // Re-pulls the caller's GitHub org map and refreshes the cached GithubAccessSnapshot.
    // Admins can pass ?userId=... to refresh another user's mirror (used to onboard an
    // invitee whose existing rows are zero). Self-refresh stays open to any authenticated
    // user so a Maintainer can re-sync after joining a new org on GitHub.
    [HttpPost("refresh-github-permissions")]
    public async Task<ActionResult<RefreshGithubAccessResponse>> RefreshGithubAccess(
        [FromQuery] Guid? userId,
        CancellationToken ct
    )
    {
        string? raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out Guid actorId))
            return Unauthorized();

        Guid target = userId ?? actorId;
        if (target != actorId && !User.IsInRole(ShieldRoles.Admin))
            return Forbid();

        GithubAccessSnapshot? snapshot = await _githubAccess.RefreshAsync(target, ct);
        if (snapshot is null)
        {
            return Ok(
                new RefreshGithubAccessResponse(
                    UserId: target,
                    SourceCount: 0,
                    Orgs: [],
                    HasGithubLogin: false
                )
            );
        }

        object details = snapshot.Fallback is null
            ? new { sourceCount = snapshot.SourceAccess.Count, orgs = snapshot.OrgMemberships }
            : new
            {
                sourceCount = snapshot.SourceAccess.Count,
                orgs = snapshot.OrgMemberships,
                usedFallback = true,
                viaAdmin = snapshot.Fallback.ViaAdminUserId,
                orgsChecked = snapshot.Fallback.OrgsChecked,
                orgsMatched = snapshot.Fallback.OrgsMatched,
            };
        await _audit.RecordAsync("access.github.refresh", "User", target.ToString(), details, ct);

        return Ok(
            new RefreshGithubAccessResponse(
                UserId: target,
                SourceCount: snapshot.SourceAccess.Count,
                Orgs: snapshot.OrgMemberships,
                HasGithubLogin: true
            )
        );
    }
}
