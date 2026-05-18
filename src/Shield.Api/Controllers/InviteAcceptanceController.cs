using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Auth.AcceptanceTickets;

namespace Shield.Api.Controllers;

// Public surface for redeeming an invite. POST /api/auth/accept-invite consumes both the
// invite token (from the email link) and an acceptance ticket (issued by the external-login
// pipeline when the device-flow poll observed an unlinked external identity).
[ApiController]
[Route("api/auth")]
public sealed class InviteAcceptanceController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly RoleManager<ShieldRole> _roleManager;
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly IAcceptanceTicketService _tickets;
    private readonly IAuditLogger _audit;
    private readonly INotificationPublisher _notifications;
    private readonly ISessionTracker _sessionTracker;
    private readonly ISessionCookieIssuer _sessionCookieIssuer;
    private readonly ISessionAuditor _sessionAuditor;
    private readonly ILogger<InviteAcceptanceController> _log;

    public InviteAcceptanceController(
        ShieldDbContext db,
        UserManager<ShieldUser> userManager,
        RoleManager<ShieldRole> roleManager,
        SignInManager<ShieldUser> signInManager,
        IAcceptanceTicketService tickets,
        IAuditLogger audit,
        INotificationPublisher notifications,
        ISessionTracker sessionTracker,
        ISessionCookieIssuer sessionCookieIssuer,
        ISessionAuditor sessionAuditor,
        ILogger<InviteAcceptanceController> log
    )
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _tickets = tickets;
        _audit = audit;
        _notifications = notifications;
        _sessionTracker = sessionTracker;
        _sessionCookieIssuer = sessionCookieIssuer;
        _sessionAuditor = sessionAuditor;
        _log = log;
    }

    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<AcceptInviteResponse>> Accept(
        [FromBody] AcceptInviteRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Invite token is required.", code = "missing_token" });

        // Two acceptance paths share this endpoint:
        //   1. Device-flow signed acceptance ticket (sibling external-login pipeline issues it)
        //   2. Already-authenticated user landing back from the OAuth auth-code popup — we
        //      synthesise an equivalent payload from their bound external login.
        AcceptanceTicketPayload? payload;
        if (!string.IsNullOrWhiteSpace(request.AcceptanceTicket))
        {
            if (!_tickets.TryValidate(request.AcceptanceTicket, out payload) || payload is null)
                return BadRequest(
                    new
                    {
                        error = "Acceptance ticket is invalid or expired.",
                        code = "ticket_invalid",
                    }
                );
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            ShieldUser? currentUser = await _userManager.GetUserAsync(User);
            if (currentUser is null)
                return Unauthorized(
                    new { error = "Session user not found.", code = "user_not_found" }
                );

            IList<UserLoginInfo> currentLogins = await _userManager.GetLoginsAsync(currentUser);
            UserLoginInfo? githubLogin = currentLogins.FirstOrDefault(login =>
                string.Equals(login.LoginProvider, "github", StringComparison.OrdinalIgnoreCase)
            );
            if (githubLogin is null)
                return BadRequest(
                    new
                    {
                        error = "No external login bound — sign in with GitHub first.",
                        code = "no_external_login",
                    }
                );
            payload = new(
                Provider: githubLogin.LoginProvider,
                SubjectId: githubLogin.ProviderKey,
                Login: githubLogin.ProviderDisplayName ?? currentUser.UserName ?? string.Empty,
                Email: currentUser.Email,
                IssuedAt: DateTimeOffset.UtcNow,
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5)
            );
        }
        else
        {
            return BadRequest(
                new
                {
                    error = "Either an acceptance ticket or an authenticated session is required.",
                    code = "no_session",
                }
            );
        }

        Invite? invite = await _db.Invites.FirstOrDefaultAsync(
            item => item.Token == request.Token,
            ct
        );
        if (invite is null)
            return BadRequest(new { error = "Invite not found." });
        if (invite.AcceptedAt is not null)
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Invite has already been accepted." }
            );
        if (invite.RevokedAt is not null)
            return StatusCode(
                StatusCodes.Status410Gone,
                new { error = "Invite has been revoked." }
            );
        if (invite.ExpiresAt <= DateTime.UtcNow)
            return StatusCode(StatusCodes.Status410Gone, new { error = "Invite has expired." });

        // Ensure the role exists in case the seed run missed it (or the role list grew).
        if (!await _roleManager.RoleExistsAsync(invite.Role))
        {
            IdentityResult roleCreate = await _roleManager.CreateAsync(new(invite.Role));
            if (!roleCreate.Succeeded)
                return Problem(
                    title: "Failed to create role",
                    detail: string.Join(", ", roleCreate.Errors.Select(error => error.Description))
                );
        }

        // Reuse an existing user when the verified external email matches; otherwise create
        // a fresh ShieldUser. Username derives from the external login so audit trails stay
        // legible. The local-auth password is random — these users sign in through the
        // external provider, not username/password.
        ShieldUser? user = string.IsNullOrEmpty(payload.Email)
            ? null
            : await _userManager.FindByEmailAsync(payload.Email);

        bool createdUser = false;
        if (user is null)
        {
            string username = BuildUsername(payload.Provider, payload.Login);
            if (await _userManager.FindByNameAsync(username) is not null)
                username = $"{username}{Guid.NewGuid().ToString("n")[..6]}";

            user = new()
            {
                UserName = username,
                Email = payload.Email ?? invite.Email,
                EmailConfirmed = !string.IsNullOrEmpty(payload.Email),
                CreatedAt = DateTime.UtcNow,
            };
            IdentityResult create = await _userManager.CreateAsync(user, GenerateRandomPassword());
            if (!create.Succeeded)
                return Problem(
                    title: "Failed to create account",
                    detail: string.Join(", ", create.Errors.Select(error => error.Description))
                );
            createdUser = true;
        }

        // Bind the external identity. AddLoginAsync is a no-op if the same provider+key already
        // exists, so resends or repeated submissions don't error.
        UserLoginInfo loginInfo = new(payload.Provider, payload.SubjectId, payload.Login);
        IList<UserLoginInfo> existingLogins = await _userManager.GetLoginsAsync(user);
        if (
            !existingLogins.Any(login =>
                login.LoginProvider == loginInfo.LoginProvider
                && login.ProviderKey == loginInfo.ProviderKey
            )
        )
        {
            IdentityResult addLogin = await _userManager.AddLoginAsync(user, loginInfo);
            if (!addLogin.Succeeded)
            {
                _log.LogWarning(
                    "Invite accept: failed to bind external login for user {UserId} provider {Provider}: {Errors}",
                    user.Id,
                    payload.Provider,
                    string.Join(", ", addLogin.Errors.Select(error => error.Description))
                );
            }
        }

        if (!await _userManager.IsInRoleAsync(user, invite.Role))
        {
            IdentityResult assign = await _userManager.AddToRoleAsync(user, invite.Role);
            if (!assign.Succeeded)
                _log.LogWarning(
                    "Invite accept: failed to assign role {Role} to user {UserId}: {Errors}",
                    invite.Role,
                    user.Id,
                    string.Join(", ", assign.Errors.Select(error => error.Description))
                );
        }

        IReadOnlyList<int> groupIds = ParseGroupIds(invite.SourceGroupIdsCsv);
        DateTime addedAt = DateTime.UtcNow;
        if (groupIds.Count > 0)
        {
            HashSet<int> existingGroups = (
                await _db
                    .GroupMemberships.Where(membership => membership.UserId == user.Id)
                    .Select(membership => membership.GroupId)
                    .ToListAsync(ct)
            ).ToHashSet();
            foreach (int groupId in groupIds.Distinct())
            {
                if (existingGroups.Contains(groupId))
                    continue;
                _db.GroupMemberships.Add(
                    new()
                    {
                        GroupId = groupId,
                        UserId = user.Id,
                        AddedAt = addedAt,
                    }
                );
            }
        }

        invite.AcceptedAt = DateTime.UtcNow;
        invite.AcceptedByUserId = user.Id;
        await _db.SaveChangesAsync(ct);

        // Audit + admin notification. Never echo the raw invite token; the invite id is the
        // public correlation handle.
        await _audit.RecordAsync(
            "access.invite.accept",
            "Invite",
            invite.Id.ToString(),
            new
            {
                userId = user.Id,
                createdUser,
                provider = payload.Provider,
                role = invite.Role,
            },
            ct
        );
        await _notifications.BroadcastAsync(
            NotificationKind.SystemMessage,
            Severity.Low,
            "Invite accepted",
            $"{user.UserName} accepted an invite ({invite.Role}).",
            relatedType: "Invite",
            relatedId: invite.Id.ToString(),
            ct
        );

        // Sign the new user in and issue the session cookie so they land on /sources already
        // authenticated. SessionCookieIssuer guarantees parity with the other three signin paths.
        await _signInManager.SignInAsync(user, isPersistent: true);
        UserSession session = await _sessionCookieIssuer.IssueAsync(HttpContext, user.Id, ct);
        await _sessionAuditor.RecordSigninAsync(user, session, SigninMethod.InviteAcceptance, ct);

        return Ok(new AcceptInviteResponse(user.Id, user.UserName!, invite.Role, groupIds));
    }

    private static string BuildUsername(string provider, string login)
    {
        // Identity's default allowed-username set is alphanumeric. Flatten any provider:login
        // pairing down to letters+digits — matches the OAuth signin path's behaviour so two
        // entry routes producing the same identity get the same UserName.
        string raw = $"{provider.ToLowerInvariant()}{login}";
        string sanitized = new(raw.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(sanitized)
            ? $"{provider.ToLowerInvariant()}{Guid.NewGuid().ToString("n")[..8]}"
            : sanitized;
    }

    private static string GenerateRandomPassword()
    {
        // 32 bytes → 256 bits. The user never sees or uses this — external login is the only
        // intended sign-in path for invitee-accepted accounts. A local-password reset stays
        // possible through standard Identity reset-token flow if they ever want one.
        Span<byte> bytes = stackalloc byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes) + "!Aa1";
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
}
