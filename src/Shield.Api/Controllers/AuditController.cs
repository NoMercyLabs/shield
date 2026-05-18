using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[NoApiToken]
public sealed class AuditController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;

    public AuditController(ShieldDbContext db, UserManager<ShieldUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    [Authorize(Policy = ShieldPolicies.Admin)]
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

        // Batch-resolve display info to avoid N×1 lookups. Actors → preferred GitHub login;
        // targets → friendly label per TargetType (Source.Name, Invite.Email, etc.).
        Dictionary<Guid, (string? Login, string? AvatarUrl)> actorMap =
            await ResolveActorDisplaysAsync(
                rows.Where(entry => entry.ActorUserId.HasValue)
                    .Select(entry => entry.ActorUserId!.Value)
                    .Distinct()
                    .ToList(),
                ct
            );
        Dictionary<(string Type, string Id), string?> targetMap = await ResolveTargetLabelsAsync(
            rows,
            ct
        );

        List<AuditEntryResponse> items = rows.Select(entry =>
            {
                (string? login, string? avatar) =
                    entry.ActorUserId.HasValue
                    && actorMap.TryGetValue(
                        entry.ActorUserId.Value,
                        out (string? Login, string? AvatarUrl) tuple
                    )
                        ? tuple
                        : (null, null);
                targetMap.TryGetValue((entry.TargetType, entry.TargetId), out string? targetLabel);
                return new AuditEntryResponse(
                    entry.Id,
                    entry.At,
                    entry.ActorUserId,
                    entry.ActorName,
                    login,
                    avatar,
                    entry.Action,
                    entry.TargetType,
                    entry.TargetId,
                    targetLabel,
                    entry.DetailsJson,
                    entry.RemoteIp
                );
            })
            .ToList();

        return Ok(new AuditPage(items, total, page, pageSize));
    }

    private async Task<
        Dictionary<Guid, (string? Login, string? AvatarUrl)>
    > ResolveActorDisplaysAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        Dictionary<Guid, (string?, string?)> result = new();
        if (userIds.Count == 0)
            return result;

        List<ShieldUser> users = await _userManager
            .Users.Where(user => userIds.Contains(user.Id))
            .ToListAsync(ct);
        foreach (ShieldUser user in users)
        {
            IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
            UserLoginInfo? github = logins.FirstOrDefault(login =>
                string.Equals(login.LoginProvider, "Github", StringComparison.OrdinalIgnoreCase)
            );
            string? avatar = github is not null
                ? $"https://avatars.githubusercontent.com/u/{github.ProviderKey}?v=4"
                : null;
            result[user.Id] = (github?.ProviderDisplayName ?? user.UserName, avatar);
        }
        return result;
    }

    private async Task<Dictionary<(string Type, string Id), string?>> ResolveTargetLabelsAsync(
        IReadOnlyList<AuditEntry> rows,
        CancellationToken ct
    )
    {
        Dictionary<(string, string), string?> result = new();
        if (rows.Count == 0)
            return result;

        // Group target ids by type so each resolve hits one query.
        HashSet<int> sourceIds = [];
        HashSet<Guid> inviteIds = [];
        HashSet<Guid> channelIds = [];
        foreach (AuditEntry entry in rows)
        {
            switch (entry.TargetType)
            {
                case "Source":
                    if (int.TryParse(entry.TargetId, out int sId))
                        sourceIds.Add(sId);
                    break;
                case "Invite":
                    if (Guid.TryParse(entry.TargetId, out Guid iId))
                        inviteIds.Add(iId);
                    break;
                case "Channel" or "AlertChannel":
                    if (Guid.TryParse(entry.TargetId, out Guid cId))
                        channelIds.Add(cId);
                    break;
            }
        }

        if (sourceIds.Count > 0)
        {
            List<(int Id, string Name)> sources = await _db
                .Sources.Where(source => sourceIds.Contains(source.Id))
                .Select(source => new ValueTuple<int, string>(source.Id, source.Name))
                .ToListAsync(ct);
            foreach ((int id, string name) in sources)
                result[("Source", id.ToString())] = name;
        }
        if (inviteIds.Count > 0)
        {
            List<(Guid Id, string Email, string? Login)> invites = await _db
                .Invites.Where(invite => inviteIds.Contains(invite.Id))
                .Select(invite => new ValueTuple<Guid, string, string?>(
                    invite.Id,
                    invite.Email,
                    invite.PreBoundLogin
                ))
                .ToListAsync(ct);
            foreach ((Guid id, string email, string? login) in invites)
                result[("Invite", id.ToString())] = !string.IsNullOrEmpty(login)
                    ? $"{login} ({email})"
                    : email;
        }
        if (channelIds.Count > 0)
        {
            List<(Guid Id, string Name)> channels = await _db
                .AlertChannels.Where(channel => channelIds.Contains(channel.Id))
                .Select(channel => new ValueTuple<Guid, string>(channel.Id, channel.Name))
                .ToListAsync(ct);
            foreach ((Guid id, string name) in channels)
            {
                result[("Channel", id.ToString())] = name;
                result[("AlertChannel", id.ToString())] = name;
            }
        }
        return result;
    }
}
