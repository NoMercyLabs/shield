using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Middleware;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly RoleManager<ShieldRole> _roleManager;
    private readonly IAppSettingsService _settings;
    private readonly ISessionTracker _sessionTracker;
    private readonly ISessionCookieIssuer _sessionCookieIssuer;
    private readonly ITwoFactorEnforcement _twoFactorEnforcement;
    private readonly IAuditLogger _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IOAuthTokenStore _oauthTokenStore;
    private readonly ISecurityEventLogger _securityLog;
    private readonly ISessionAuditor _sessionAuditor;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly Microsoft.AspNetCore.Antiforgery.IAntiforgery _antiforgery;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        SignInManager<ShieldUser> signInManager,
        UserManager<ShieldUser> userManager,
        RoleManager<ShieldRole> roleManager,
        IAppSettingsService settings,
        ISessionTracker sessionTracker,
        ISessionCookieIssuer sessionCookieIssuer,
        ITwoFactorEnforcement twoFactorEnforcement,
        IAuditLogger audit,
        INotificationPublisher notifications,
        IOAuthTokenStore oauthTokenStore,
        ISecurityEventLogger securityLog,
        ISessionAuditor sessionAuditor,
        IJwtIssuer jwtIssuer,
        Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
        ILogger<AuthController> log
    )
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _roleManager = roleManager;
        _settings = settings;
        _sessionTracker = sessionTracker;
        _sessionCookieIssuer = sessionCookieIssuer;
        _twoFactorEnforcement = twoFactorEnforcement;
        _audit = audit;
        _notifications = notifications;
        _oauthTokenStore = oauthTokenStore;
        _securityLog = securityLog;
        _sessionAuditor = sessionAuditor;
        _jwtIssuer = jwtIssuer;
        _antiforgery = antiforgery;
        _log = log;
    }

    // Returns the XSRF request token and sets the XSRF-TOKEN cookie so the SPA can
    // bootstrap its Axios XSRF interceptor. Call once after login; re-call if the cookie
    // goes missing (tab restore, manual cookie clear).
    [HttpGet("xsrf")]
    [Authorize]
    [NoApiToken]
    public IActionResult Xsrf()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    // Issues a JWT for headless API clients. Embeds the user's current SecurityStamp so the
    // token is automatically invalidated on password change / 2FA toggle / lockout — no
    // separate blocklist needed. The SPA always uses cookies; this endpoint is for non-browser
    // callers that can't carry a cookie jar.
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<TokenResponse>> Token(
        [FromBody] LoginRequest request,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Password)
        )
            return BadRequest(new { error = "Username and password are required." });

        ShieldUser? user = await _userManager.FindByNameAsync(request.Username);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            if (user is not null)
                await _userManager.AccessFailedAsync(user);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized(new { error = "Account locked." });

        if (user.TwoFactorEnabled && string.IsNullOrWhiteSpace(request.TwoFactorCode))
            return Unauthorized(
                new { error = "Two-factor code required.", requiresTwoFactor = true }
            );

        if (user.TwoFactorEnabled && !string.IsNullOrWhiteSpace(request.TwoFactorCode))
        {
            bool tfaValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                request.TwoFactorCode
            );
            if (!tfaValid)
                return Unauthorized(new { error = "Invalid 2FA code." });
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        string jwt = await _jwtIssuer.IssueAsync(user, ct);
        IList<string> roles = await _userManager.GetRolesAsync(user);
        return Ok(new TokenResponse(user.Id.ToString(), user.UserName, roles.ToList(), jwt));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        SignInResult result = await _signInManager.PasswordSignInAsync(
            request.Username,
            request.Password,
            isPersistent: true,
            lockoutOnFailure: true
        );

        if (result.RequiresTwoFactor)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Unauthorized(
                    new LoginResponse(
                        UserId: null,
                        Username: null,
                        Roles: [],
                        RequiresTwoFactor: true
                    )
                );

            SignInResult twoFactor = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                request.TwoFactorCode,
                isPersistent: true,
                rememberClient: false
            );
            if (!twoFactor.Succeeded)
                return Unauthorized(
                    new LoginResponse(
                        UserId: null,
                        Username: null,
                        Roles: [],
                        RequiresTwoFactor: true,
                        Error: "Invalid 2FA code"
                    )
                );
        }
        else if (result.IsLockedOut)
        {
            await TryLogSecurityAsync(
                "login.lockout",
                Severity.High,
                request.Username,
                ct: HttpContext.RequestAborted
            );
            return Unauthorized(
                new LoginResponse(UserId: null, Username: null, Roles: [], Error: "Account locked")
            );
        }
        else if (!result.Succeeded)
        {
            await TryLogSecurityAsync(
                "login.failed",
                Severity.Medium,
                request.Username,
                ct: HttpContext.RequestAborted
            );
            return Unauthorized(
                new LoginResponse(
                    UserId: null,
                    Username: null,
                    Roles: [],
                    Error: "Invalid credentials"
                )
            );
        }

        ShieldUser user = (await _userManager.FindByNameAsync(request.Username))!;
        IList<string> roles = await _userManager.GetRolesAsync(user);
        UserSession session = await _sessionCookieIssuer.IssueAsync(
            HttpContext,
            user.Id,
            HttpContext.RequestAborted
        );
        await _sessionAuditor.RecordSigninAsync(
            user,
            session,
            SigninMethod.Password,
            HttpContext.RequestAborted
        );
        return Ok(
            new LoginResponse(user.Id.ToString(), user.UserName, roles.ToList(), Succeeded: true)
        );
    }

    // First-user-wins: when the users table is empty the first registration becomes Admin and
    // auto-creates the Admin role if missing. Subsequent registrations land in Viewer.
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
    {
        if (
            string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Password)
        )
            return BadRequest(new { error = "Username and password are required" });

        bool isFirstUser = !await _userManager.Users.AnyAsync();
        string assignedRole = isFirstUser ? ShieldRoles.Admin : ShieldRoles.Viewer;

        if (!await _roleManager.RoleExistsAsync(assignedRole))
        {
            IdentityResult roleCreate = await _roleManager.CreateAsync(new(assignedRole));
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
            EmailConfirmed = string.IsNullOrWhiteSpace(request.Email),
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            return BadRequest(
                new { error = string.Join(", ", create.Errors.Select(error => error.Description)) }
            );

        IdentityResult assign = await _userManager.AddToRoleAsync(user, assignedRole);
        if (!assign.Succeeded)
            return Problem(
                title: "Failed to assign role",
                detail: string.Join(", ", assign.Errors.Select(error => error.Description))
            );

        IList<string> roles = await _userManager.GetRolesAsync(user);
        return CreatedAtAction(
            nameof(Me),
            new RegisterResponse(user.Id.ToString(), user.UserName!, roles.ToList())
        );
    }

    // Anonymous discovery so the SPA can render OAuth signin buttons before the user is authenticated.
    [HttpGet("providers")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthProvidersResponse>> Providers(CancellationToken ct)
    {
        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        List<AuthProviderInfo> providers = [];
        if (IsConfigured(snapshot.GithubOAuth))
            providers.Add(
                new("github", "GitHub", "https://github.githubassets.com/favicons/favicon.svg")
            );
        if (IsConfigured(snapshot.SlackOAuth))
            providers.Add(
                new(
                    "slack",
                    "Slack",
                    "https://a.slack-edge.com/80588/marketing/img/meta/slack_hash_256.png"
                )
            );
        if (IsConfigured(snapshot.GoogleOAuth))
            providers.Add(new("google", "Google", "https://www.google.com/favicon.ico"));
        return Ok(new AuthProvidersResponse(providers));
    }

    private static bool IsConfigured(OAuthClientSettings client) =>
        !string.IsNullOrEmpty(client.ClientId) && !string.IsNullOrEmpty(client.ClientSecret);

    [HttpGet("setup-required")]
    [AllowAnonymous]
    [NoApiToken]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<SetupRequiredResponse>> SetupRequired(CancellationToken ct)
    {
        bool required = !await _userManager.Users.AnyAsync(ct);
        return Ok(new SetupRequiredResponse(required));
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    [NoApiToken]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<LoginResponse>> Setup(
        [FromBody] SetupRequest request,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Password)
        )
            return BadRequest(new { error = "Username and password are required." });

        if (await _userManager.Users.AnyAsync(ct))
            return Conflict(new { error = "Setup already completed." });

        if (!await _roleManager.RoleExistsAsync(ShieldRoles.Admin))
        {
            IdentityResult roleResult = await _roleManager.CreateAsync(new(ShieldRoles.Admin));
            if (!roleResult.Succeeded)
                return Problem(
                    title: "Failed to create Admin role",
                    detail: string.Join(", ", roleResult.Errors.Select(error => error.Description))
                );
        }

        ShieldUser user = new()
        {
            UserName = request.Username,
            Email = request.Email,
            EmailConfirmed = string.IsNullOrWhiteSpace(request.Email),
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            return BadRequest(
                new { error = string.Join(", ", create.Errors.Select(error => error.Description)) }
            );

        IdentityResult assign = await _userManager.AddToRoleAsync(user, ShieldRoles.Admin);
        if (!assign.Succeeded)
            return Problem(
                title: "Failed to assign Admin role",
                detail: string.Join(", ", assign.Errors.Select(error => error.Description))
            );

        await _signInManager.SignInAsync(user, isPersistent: true);
        IList<string> roles = await _userManager.GetRolesAsync(user);
        UserSession session = await _sessionCookieIssuer.IssueAsync(HttpContext, user.Id, ct);
        await _sessionAuditor.RecordSigninAsync(user, session, SigninMethod.Password, ct);
        return Ok(
            new LoginResponse(user.Id.ToString(), user.UserName, roles.ToList(), Succeeded: true)
        );
    }

    [HttpPost("logout")]
    [Authorize]
    [NoApiToken]
    public async Task<IActionResult> Logout()
    {
        // Revoke the current session row + nuke its cookie so the row can't be replayed.
        // The Identity cookie clears as part of SignOutAsync. SecurityStamp is intentionally
        // NOT bumped here — that would invalidate every sibling session on the user. Logout
        // is "kill the current browser", not "kick every device".
        if (
            HttpContext.Items[SessionTrackingMiddleware.ContextItemKey]
            is UserSession currentSession
        )
        {
            await _sessionTracker.RevokeAsync(currentSession.Id, HttpContext.RequestAborted);
            try
            {
                await _audit.RecordAsync(
                    "auth.session.revoke",
                    "UserSession",
                    currentSession.Id.ToString(),
                    new { reason = "logout", userId = currentSession.UserId },
                    HttpContext.RequestAborted
                );
            }
            catch (Exception ex)
            {
                _log.LogBestEffortFailure(ex);
            }
        }
        HttpContext.Response.Cookies.Delete(SessionTrackingMiddleware.CookieName);
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    // Changes the caller's password. Identity bumps the SecurityStamp automatically inside
    // ChangePasswordAsync, so sibling cookies invalidate at the next SecurityStampValidator
    // tick (we tightened that to 1 minute in Program.cs). We also explicitly revoke every
    // OTHER UserSession row so the kill is immediate, not eventually-consistent.
    [HttpPost("password")]
    [Authorize]
    [NoApiToken]
    [EnableRateLimiting("auth-burst")]
    [RequireOriginalIdentity]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.CurrentPassword)
            || string.IsNullOrWhiteSpace(request.NewPassword)
        )
            return BadRequest(new { error = "Current and new password are required." });

        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        IdentityResult result = await _userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword
        );
        if (!result.Succeeded)
            return BadRequest(
                new { error = string.Join(", ", result.Errors.Select(error => error.Description)) }
            );

        // ChangePasswordAsync already updated the SecurityStamp. Belt + braces:
        await _userManager.UpdateSecurityStampAsync(user);

        // Hard-revoke every sibling session row so they can't be touched (TouchAsync would also
        // refresh LastActiveAt of a still-decryptable old cookie until the SecurityStamp
        // validator runs). Keep the current session alive — the caller stays signed in.
        Guid currentSessionId =
            (HttpContext.Items[SessionTrackingMiddleware.ContextItemKey] as UserSession)?.Id
            ?? Guid.Empty;
        int revoked = await _sessionTracker.RevokeOthersAsync(user.Id, currentSessionId, ct);

        // Re-sign-in so this browser picks up the bumped SecurityStamp on the next request
        // (without this, the current cookie is also stale and the middleware bounces this user).
        await _signInManager.RefreshSignInAsync(user);

        try
        {
            await _audit.RecordAsync(
                "auth.password.change",
                "User",
                user.Id.ToString(),
                new { revokedSiblingSessions = revoked },
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        string changeIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown IP";

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "password.changed",
                severity: Severity.High,
                remoteIp: changeIp,
                userAgent: HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua1
                    ? ua1
                    : null,
                userName: user.UserName,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.High,
                    Title = "Password changed",
                    Body = $"Your password was changed from {changeIp} at {DateTime.UtcNow:u}.",
                    RelatedType = "User",
                    RelatedId = user.Id.ToString(),
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

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken ct)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        IList<string> roles = await _userManager.GetRolesAsync(user);

        // Decorate with code-host profile info when the user has either bound an external
        // identity (AspNetUserLogins — invite/accept path) OR is the admin who connected
        // a code-host token (IntegrationToken with LinkedUserId — connect flow). Prefer the
        // bound login since it survives token rotation; fall back to the connect-flow row.
        string? providerKey = null;
        string? providerLogin = null;
        string? displayName = null;

        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        UserLoginInfo? githubLogin = logins.FirstOrDefault(login =>
            string.Equals(login.LoginProvider, "github", StringComparison.OrdinalIgnoreCase)
        );
        if (githubLogin is not null)
        {
            providerKey = githubLogin.ProviderKey;
            providerLogin = githubLogin.ProviderDisplayName;
            displayName = githubLogin.ProviderDisplayName;
        }
        else
        {
            OAuthTokenSnapshot? snapshot = await _oauthTokenStore.GetAsync(
                OAuthProvider.Github,
                ct
            );
            if (snapshot is not null && !string.IsNullOrEmpty(snapshot.AccountLogin))
            {
                providerKey = snapshot.AccountId;
                providerLogin = snapshot.AccountLogin;
                displayName = snapshot.AccountLogin;
            }
        }

        string? avatarUrl = !string.IsNullOrEmpty(providerKey)
            ? $"https://avatars.githubusercontent.com/u/{providerKey}?v=4"
            : null;
        string? profileUrl = !string.IsNullOrEmpty(providerLogin)
            ? $"https://github.com/{providerLogin}"
            : null;

        string? impersonatedBy = User.FindFirstValue(
            RequireOriginalIdentityAttribute.ImpersonatorClaimType
        );
        string? impersonatorLogin = User.FindFirstValue("imp.admin.name");

        // Side-effect: ensure the XSRF-TOKEN cookie is set on every authenticated /me call so
        // the SPA's Axios XSRF interceptor has something to read on subsequent mutating
        // requests. Real cookie-auth sessions need this to send X-XSRF-TOKEN on logout/etc.
        _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(
            new MeResponse(
                user.Id.ToString(),
                user.UserName,
                roles.ToList(),
                DisplayName: displayName,
                AvatarUrl: avatarUrl,
                ProfileUrl: profileUrl,
                ProviderLogin: providerLogin,
                ProviderKey: providerKey,
                ImpersonatedBy: impersonatedBy,
                ImpersonatorLogin: impersonatorLogin
            )
        );
    }

    [HttpPost("2fa/enroll")]
    [Authorize]
    [NoApiToken]
    [RequireOriginalIdentity]
    public async Task<ActionResult<TwoFactorEnrollFullResponse>> EnrollTwoFactor()
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        // Reset every enroll call — operators re-running the flow expect a fresh secret.
        await _userManager.ResetAuthenticatorKeyAsync(user);
        string? key = await _userManager.GetAuthenticatorKeyAsync(user);
        string sharedKey = key ?? string.Empty;

        // Recovery codes are persisted hashed by Identity (per-user token store). Eight is
        // the spec ask; mirrors what GitHub/GitLab default to.
        IEnumerable<string>? recovery = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            8
        );
        IReadOnlyList<string> codes = (recovery ?? []).ToList();

        string uri = BuildAuthenticatorUri(user.UserName ?? user.Email ?? "shield-user", sharedKey);
        return Ok(new TwoFactorEnrollFullResponse(sharedKey, uri, codes));
    }

    [HttpPost("2fa/verify")]
    [Authorize]
    [NoApiToken]
    [RequireOriginalIdentity]
    public async Task<ActionResult<TwoFactorVerifyResponse>> VerifyTwoFactor(
        [FromBody] TwoFactorVerifyRequest request
    )
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        bool valid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            request.Code
        );
        if (!valid)
            return BadRequest(new { error = "Invalid code" });

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        IEnumerable<string>? recovery = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            10
        );
        IReadOnlyList<string> codes = (recovery ?? []).ToList();

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "twofactor.enrolled",
                severity: Severity.Low,
                userName: user.UserName,
                ct: HttpContext.RequestAborted
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.Low,
                    Title = "Two-factor authentication enabled",
                    Body = "Two-factor authentication enabled.",
                    RelatedType = "User",
                    RelatedId = user.Id.ToString(),
                    CreatedAt = DateTime.UtcNow,
                },
                HttpContext.RequestAborted
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        return Ok(new TwoFactorVerifyResponse(codes));
    }

    [HttpGet("2fa/status")]
    [Authorize]
    public async Task<ActionResult<TwoFactorStatusResponse>> TwoFactorStatus(CancellationToken ct)
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        bool requiredByPolicy = await _twoFactorEnforcement.IsRequiredAsync(ct);
        int remaining = await _userManager.CountRecoveryCodesAsync(user);
        return Ok(new TwoFactorStatusResponse(user.TwoFactorEnabled, requiredByPolicy, remaining));
    }

    // Lets a user sign in with a one-shot recovery code when they've lost their TOTP device.
    // Each code is single-use — Identity hashes them in AspNetUserTokens and clears on use.
    [HttpPost("2fa/recovery")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<ActionResult<LoginResponse>> TwoFactorRecovery(
        [FromBody] TwoFactorRecoveryRequest request
    )
    {
        if (
            string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.RecoveryCode)
        )
            return BadRequest(new { error = "Username and recovery code are required" });

        // Without a prior password sign-in we can't use TwoFactorRecoveryCodeSignInAsync, so we
        // verify the recovery code via UserManager (which marks it consumed) then sign in cookie.
        ShieldUser? user = await _userManager.FindByNameAsync(request.Username);
        if (user is null)
            return Unauthorized(new LoginResponse(null, null, [], Error: "Invalid recovery code"));

        IdentityResult redeem = await _userManager.RedeemTwoFactorRecoveryCodeAsync(
            user,
            request.RecoveryCode
        );
        if (!redeem.Succeeded)
            return Unauthorized(new LoginResponse(null, null, [], Error: "Invalid recovery code"));

        await _signInManager.SignInAsync(user, isPersistent: true);
        IList<string> roles = await _userManager.GetRolesAsync(user);
        UserSession session = await _sessionCookieIssuer.IssueAsync(
            HttpContext,
            user.Id,
            HttpContext.RequestAborted
        );
        await _sessionAuditor.RecordSigninAsync(
            user,
            session,
            SigninMethod.RecoveryCode,
            HttpContext.RequestAborted
        );
        return Ok(new LoginResponse(user.Id.ToString(), user.UserName, roles.ToList()));
    }

    // Removes 2FA from the caller's account. Requires the current password to short-circuit a
    // cookie-stealer; if `auth.require_2fa` is on, only an Admin can disable (and they self-rescue
    // their own enrollment via the QR flow).
    [HttpPost("2fa/disable")]
    [Authorize]
    [NoApiToken]
    [RequireOriginalIdentity]
    public async Task<IActionResult> DisableTwoFactor(
        [FromBody] TwoFactorDisableRequest request,
        CancellationToken ct
    )
    {
        ShieldUser? user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        bool requiredByPolicy = await _twoFactorEnforcement.IsRequiredAsync(ct);
        if (requiredByPolicy)
        {
            bool isAdmin = await _userManager.IsInRoleAsync(user, ShieldRoles.Admin);
            if (!isAdmin)
                return Forbid();
        }

        bool valid = await _userManager.CheckPasswordAsync(user, request.CurrentPassword);
        if (!valid)
            return BadRequest(new { error = "Invalid password" });

        IdentityResult result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
            return Problem(
                title: "Failed to disable 2FA",
                detail: string.Join(", ", result.Errors.Select(error => error.Description))
            );

        // Bump the SecurityStamp so any sibling cookies (other devices) invalidate next request.
        await _userManager.UpdateSecurityStampAsync(user);
        await _userManager.ResetAuthenticatorKeyAsync(user);

        try
        {
            await _audit.RecordAsync(
                "auth.2fa.disable",
                "User",
                user.Id.ToString(),
                details: null,
                ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        string disableIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown IP";

        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: "twofactor.disabled",
                severity: Severity.High,
                remoteIp: disableIp,
                userAgent: HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua2
                    ? ua2
                    : null,
                userName: user.UserName,
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _log.LogBestEffortFailure(ex);
        }

        try
        {
            await _notifications.PublishAsync(
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Kind = NotificationKind.SystemMessage,
                    Severity = Severity.High,
                    Title = "Two-factor authentication disabled",
                    Body =
                        $"Two-factor authentication disabled from {disableIp} at {DateTime.UtcNow:u}.",
                    RelatedType = "User",
                    RelatedId = user.Id.ToString(),
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

    private static string BuildAuthenticatorUri(string username, string sharedKey)
    {
        // Explicit algorithm + period in addition to secret/issuer/digits. Most authenticator
        // apps default to SHA1 / 30s when omitted, but a few (FreeOTP, some 1Password setups)
        // require them spelled out or they pick a different mode and the codes never match.
        // Uri.EscapeDataString uses RFC 3986 percent-encoding — HttpUtility.UrlEncode uses
        // `+` for space, which authenticator apps treat as a literal plus in the label.
        StringBuilder builder = new();
        builder.Append("otpauth://totp/Shield:");
        builder.Append(Uri.EscapeDataString(username));
        builder.Append("?secret=");
        builder.Append(sharedKey);
        builder.Append("&issuer=Shield&algorithm=SHA1&digits=6&period=30");
        return builder.ToString();
    }

    // Security observation must never fail the auth response — swallow + log the inner
    // exception. The bell + push pipeline is best-effort by design.
    private async Task TryLogSecurityAsync(
        string eventType,
        Severity severity,
        string? userName,
        CancellationToken ct
    )
    {
        try
        {
            await _securityLog.LogAsync(
                source: "shield.auth",
                eventType: eventType,
                severity: severity,
                remoteIp: HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? ua
                    : null,
                userName: userName,
                path: HttpContext.Request.Path.Value,
                ct: ct
            );
        }
        catch
        {
            // Observation failure must not bubble to the user.
        }
    }
}
