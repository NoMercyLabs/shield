using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Middleware;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class SessionsController : ControllerBase
{
    private readonly ISessionTracker _tracker;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly ShieldDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly INotificationPublisher _notifications;
    private readonly ISecurityEventLogger _securityLog;
    private readonly ILogger<SessionsController> _log;

    public SessionsController(
        ISessionTracker tracker,
        UserManager<ShieldUser> userManager,
        SignInManager<ShieldUser> signInManager,
        ShieldDbContext db,
        IAuditLogger audit,
        INotificationPublisher notifications,
        ISecurityEventLogger securityLog,
        ILogger<SessionsController> log
    )
    {
        _tracker = tracker;
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _securityLog = securityLog;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult<SessionListResponse>> List(
        [FromQuery] bool all = false,
        CancellationToken ct = default
    )
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        bool isAdmin = await _userManager.IsInRoleAsync(user, ShieldRoles.Admin);
        IReadOnlyList<UserSession> sessions =
            all && isAdmin
                ? await _tracker.ListAllAsync(ct)
                : await _tracker.ListAsync(user.Id, ct);

        UserSession? current =
            HttpContext.Items[SessionTrackingMiddleware.ContextItemKey] as UserSession;
        Guid currentId = current?.Id ?? Guid.Empty;

        // For admin "all" view, name lookup needs a single hop instead of N user lookups.
        Dictionary<Guid, string?> nameLookup;
        if (all && isAdmin)
        {
            Guid[] userIds = sessions.Select(session => session.UserId).Distinct().ToArray();
            nameLookup = await _db
                .Users.Where(other => userIds.Contains(other.Id))
                .ToDictionaryAsync(other => other.Id, other => other.UserName, ct);
        }
        else
        {
            nameLookup = new() { [user.Id] = user.UserName };
        }

        List<SessionInfo> infos = sessions
            .Select(session => new SessionInfo(
                Id: session.Id,
                UserId: session.UserId,
                Username: nameLookup.TryGetValue(session.UserId, out string? name) ? name : null,
                UserAgent: session.UserAgent,
                RemoteIp: session.RemoteIp,
                CreatedAt: session.CreatedAt,
                LastActiveAt: session.LastActiveAt,
                IsCurrent: session.Id == currentId
            ))
            .ToList();

        return Ok(new SessionListResponse(infos));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        UserSession? session = await _db.UserSessions.FirstOrDefaultAsync(row => row.Id == id, ct);
        if (session is null)
            return NotFound();

        bool isAdmin = await _userManager.IsInRoleAsync(user, ShieldRoles.Admin);
        if (session.UserId != user.Id && !isAdmin)
            return Forbid();

        await _tracker.RevokeAsync(id, ct);
        try
        {
            await _audit.RecordAsync(
                "auth.session.revoke",
                "UserSession",
                id.ToString(),
                new { targetUserId = session.UserId, reason = "manual" },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "session.revoked",
                severity: Severity.Low,
                userAgent: session.UserAgent,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            string agentLabel = string.IsNullOrWhiteSpace(session.UserAgent)
                ? "unknown device"
                : session.UserAgent;
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = session.UserId,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = "Session revoked",
                    Body = $"Session from '{agentLabel}' revoked.",
                    RelatedType = "UserSession",
                    RelatedId = id.ToString(),
                    CreatedAt = DateTime.UtcNow,
                },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        return NoContent();
    }

    [HttpPost("revoke-others")]
    public async Task<ActionResult<RevokeOthersResponse>> RevokeOthers(CancellationToken ct)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        UserSession? current =
            HttpContext.Items[SessionTrackingMiddleware.ContextItemKey] as UserSession;
        // No current session cookie → there's nothing to "keep", so we revoke everything for the user.
        Guid keepId = current?.Id ?? Guid.Empty;

        int revoked = await _tracker.RevokeOthersAsync(user.Id, keepId, ct);
        try
        {
            await _audit.RecordAsync(
                "auth.session.revoke_others",
                "User",
                user.Id.ToString(),
                new { revoked, keptSessionId = keepId },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }
        return Ok(new RevokeOthersResponse(revoked));
    }

    // "Sign out everywhere" panic button. Revokes EVERY session row for the caller (including
    // the current one), bumps the SecurityStamp so the Identity cookie also stops authenticating
    // siblings within the next validator tick (1 min — see Program.cs), signs the current
    // request out, and clears the session cookie. Caller MUST re-authenticate from scratch.
    [HttpPost("revoke-all")]
    [EnableRateLimiting("auth-burst")]
    [RequireOriginalIdentity]
    public async Task<ActionResult<RevokeOthersResponse>> RevokeAll(CancellationToken ct)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        int revoked = await _tracker.RevokeAllAsync(user.Id, ct);

        // Bump SecurityStamp so the Identity application cookie on any sibling browser stops
        // authenticating at the next SecurityStampValidator tick — defense in depth against a
        // race where a sibling fires a request between the row revoke and the cookie kill.
        await _userManager.UpdateSecurityStampAsync(user);

        // Drop our own cookies + Identity sign-out so the caller is fully logged out too.
        HttpContext.Response.Cookies.Delete(SessionTrackingMiddleware.CookieName);
        await _signInManager.SignOutAsync();

        try
        {
            await _audit.RecordAsync(
                "auth.session.revoke_all",
                "User",
                user.Id.ToString(),
                new { revoked },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }
        return Ok(new RevokeOthersResponse(revoked));
    }
}
