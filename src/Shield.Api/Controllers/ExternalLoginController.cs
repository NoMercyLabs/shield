using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Auth.AcceptanceTickets;
using Shield.Api.Auth.External;
using Shield.Api.Middleware;

namespace Shield.Api.Controllers;

// "Sign in with X" surface. Distinct from OAuthController:
//   - OAuthController runs the CONNECT flow (admin-only, writes IntegrationToken so the
//     source scanner gets a bearer for api.github.com).
//   - This controller runs the SIGNIN flow (anonymous, walks the user through the same
//     GitHub device-code dance, looks up AspNetUserLogins, and either signs them in or
//     emits a `needsInvite: true` payload for the admin).
//
// Endpoints are intentionally provider-agnostic: adding GitLab / Gitea / Forgejo later is
// a matter of registering a new IExternalLoginProvider — no controller edits.
[ApiController]
[Route("api/auth/external")]
[EnableRateLimiting("auth-burst")]
public sealed class ExternalLoginController : ControllerBase
{
    private readonly IExternalLoginProviderRegistry _registry;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly ISessionTracker _sessionTracker;
    private readonly ISessionCookieIssuer _sessionCookieIssuer;
    private readonly IAuditLogger _audit;
    private readonly IAcceptanceTicketService _acceptanceTickets;
    private readonly ISessionAuditor _sessionAuditor;
    private readonly ILogger<ExternalLoginController> _logger;

    public ExternalLoginController(
        IExternalLoginProviderRegistry registry,
        UserManager<ShieldUser> userManager,
        SignInManager<ShieldUser> signInManager,
        ISessionTracker sessionTracker,
        ISessionCookieIssuer sessionCookieIssuer,
        IAuditLogger audit,
        IAcceptanceTicketService acceptanceTickets,
        ISessionAuditor sessionAuditor,
        ILogger<ExternalLoginController> logger
    )
    {
        _registry = registry;
        _userManager = userManager;
        _signInManager = signInManager;
        _sessionTracker = sessionTracker;
        _sessionCookieIssuer = sessionCookieIssuer;
        _audit = audit;
        _acceptanceTickets = acceptanceTickets;
        _sessionAuditor = sessionAuditor;
        _logger = logger;
    }

    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<ExternalLoginProvidersResponse> Providers()
    {
        List<ExternalLoginProviderInfo> infos = _registry
            .All.Select(provider => new ExternalLoginProviderInfo(
                provider.Key,
                provider.DisplayName,
                provider.IconKey
            ))
            .ToList();
        return Ok(new ExternalLoginProvidersResponse(infos));
    }

    [HttpPost("{provider}/start")]
    [AllowAnonymous]
    public async Task<ActionResult<ExternalLoginStartResponse>> Start(
        string provider,
        [FromBody] ExternalLoginStartRequest? request,
        CancellationToken ct
    )
    {
        if (!_registry.TryResolve(provider, out IExternalLoginProvider adapter))
            return NotFound(new { error = "unknown_provider" });

        string returnPath = SanitizeReturn(request?.ReturnPath);

        try
        {
            ExternalLoginStartResult result = await adapter.StartSigninAsync(returnPath, ct);
            return Ok(
                new ExternalLoginStartResponse(
                    result.FlowId,
                    result.UserCode,
                    result.VerificationUri,
                    result.VerificationUriComplete,
                    result.Interval,
                    result.ExpiresIn
                )
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "External-login start transport failure for provider {Provider}",
                adapter.Key
            );
            return StatusCode(502, new { error = "provider_unreachable" });
        }
    }

    [HttpPost("{provider}/poll")]
    [AllowAnonymous]
    public async Task<ActionResult<ExternalLoginPollResponse>> Poll(
        string provider,
        [FromBody] ExternalLoginPollRequest request,
        CancellationToken ct
    )
    {
        if (!_registry.TryResolve(provider, out IExternalLoginProvider adapter))
            return NotFound(new { error = "unknown_provider" });

        if (request is null || string.IsNullOrWhiteSpace(request.FlowId))
            return BadRequest(new { error = "missing_flow_id" });

        ExternalLoginPollResult poll = await adapter.PollSigninAsync(request.FlowId, ct);

        switch (poll.Status)
        {
            case ExternalLoginPollStatus.Pending:
                return Accepted(new ExternalLoginPollResponse("pending"));
            case ExternalLoginPollStatus.SlowDown:
                return Accepted(new ExternalLoginPollResponse("slow_down"));
            case ExternalLoginPollStatus.Expired:
                return StatusCode(410, new ExternalLoginPollResponse("expired"));
            case ExternalLoginPollStatus.Denied:
                return StatusCode(403, new ExternalLoginPollResponse("denied"));
            case ExternalLoginPollStatus.Error:
                return StatusCode(502, new ExternalLoginPollResponse("error"));
            case ExternalLoginPollStatus.Ok:
                return await HandleOkAsync(adapter, poll.Identity!, ct);
            default:
                return StatusCode(500, new ExternalLoginPollResponse("error"));
        }
    }

    private async Task<ActionResult<ExternalLoginPollResponse>> HandleOkAsync(
        IExternalLoginProvider adapter,
        ExternalIdentity identity,
        CancellationToken ct
    )
    {
        // AspNetUserLogins is the Identity-native external-login table. The composite key
        // (LoginProvider, ProviderKey) lets one ShieldUser carry many external identities
        // (github + gitlab + bitbucket) without bespoke schema.
        ShieldUser? user = await _userManager.FindByLoginAsync(adapter.Key, identity.SubjectId);

        string? remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (user is null)
        {
            // Owner-invite-only: do NOT auto-provision. The InviteService (sibling agent)
            // is the only path that creates a ShieldUser + AddLoginAsync. SPA surfaces the
            // captured identity so the admin can copy-paste it into the invite.
            await _audit.RecordAsync(
                "auth.external.needs_invite",
                "ExternalIdentity",
                $"{identity.Provider}:{identity.SubjectId}",
                details: new
                {
                    provider = identity.Provider,
                    login = identity.Login,
                    avatarUrl = identity.AvatarUrl,
                    remoteIp,
                },
                ct: ct
            );
            string ticket = _acceptanceTickets.Issue(
                new(
                    identity.Provider,
                    identity.SubjectId,
                    identity.Login,
                    identity.Email,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddMinutes(5)
                )
            );
            return Ok(
                new ExternalLoginPollResponse(
                    Status: "ok",
                    NeedsInvite: true,
                    Identity: new(
                        identity.Provider,
                        identity.Login,
                        identity.Email,
                        identity.AvatarUrl
                    ),
                    AcceptanceTicket: ticket
                )
            );
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        UserSession session = await _sessionCookieIssuer.IssueAsync(HttpContext, user.Id, ct);

        try
        {
            await _audit.RecordAsync(
                "auth.external.signin",
                "User",
                user.Id.ToString(),
                details: new
                {
                    provider = identity.Provider,
                    subjectId = identity.SubjectId,
                    login = identity.Login,
                    remoteIp,
                    sessionId = session.Id,
                },
                ct: ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogBestEffortFailure(ex);
        }

        SigninMethod externalMethod = identity.Provider.ToLowerInvariant() switch
        {
            "github" => SigninMethod.GithubOAuth,
            "google" => SigninMethod.GoogleOAuth,
            "slack" => SigninMethod.SlackOAuth,
            _ => SigninMethod.GithubOAuth,
        };
        await _sessionAuditor.RecordSigninAsync(user, session, externalMethod, ct);

        return Ok(new ExternalLoginPollResponse(Status: "ok"));
    }

    private static string SanitizeReturn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "/";
        // Local same-origin only; reject scheme+host paths so an attacker can't smuggle a
        // phishing target through the start endpoint.
        if (!raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal))
            return "/";
        return raw;
    }
}
