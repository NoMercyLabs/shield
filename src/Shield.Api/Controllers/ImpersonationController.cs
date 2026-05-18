using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Api.Middleware;
using Shield.Api.Services;
using Shield.Data.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/impersonation")]
public sealed class ImpersonationController : ControllerBase
{
    private readonly UserManager<ShieldUser> _userManager;
    private readonly IImpersonationCookieIssuer _issuer;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ImpersonationController> _log;

    public ImpersonationController(
        UserManager<ShieldUser> userManager,
        IImpersonationCookieIssuer issuer,
        IAuditLogger audit,
        ILogger<ImpersonationController> log
    )
    {
        _userManager = userManager;
        _issuer = issuer;
        _audit = audit;
        _log = log;
    }

    [HttpPost("start")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    [RequireOriginalIdentity]
    public async Task<ActionResult<ImpersonationStartResponse>> Start(
        [FromBody] ImpersonationStartRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(request.UserId, out Guid targetId))
            return BadRequest(new { error = "invalid_user_id" });

        string? rawCallerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawCallerId, out Guid callerId))
            return Unauthorized();
        if (callerId == targetId)
            return BadRequest(new { error = "cannot_impersonate_self" });

        ShieldUser? target = await _userManager.FindByIdAsync(targetId.ToString());
        if (target is null)
            return NotFound(new { error = "user_not_found" });

        IList<string> targetRoles = await _userManager.GetRolesAsync(target);
        if (targetRoles.Contains(ShieldRoles.Admin))
            return BadRequest(new { error = "cannot_impersonate_admin" });

        ImpersonationPayload payload = new(
            AdminUserId: callerId,
            ImpersonatedUserId: target.Id,
            IssuedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        string cookieValue = _issuer.Protect(payload);

        bool requireHttps = HttpContext.Request.IsHttps;
        HttpContext.Response.Cookies.Append(
            ImpersonationMiddleware.CookieName,
            cookieValue,
            new()
            {
                HttpOnly = true,
                Secure = requireHttps,
                SameSite = requireHttps ? SameSiteMode.Strict : SameSiteMode.Lax,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.Add(ImpersonationCookieIssuer.MaxLifetime),
                Path = "/",
            }
        );

        try
        {
            await _audit.RecordAsync(
                "auth.impersonate.start",
                "User",
                target.Id.ToString(),
                new
                {
                    targetUserId = target.Id,
                    targetLogin = target.UserName,
                    remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                },
                ct
            );
        }
        catch
        {
            // Audit failure must not block the override.
        }

        return Ok(new ImpersonationStartResponse(target.Id, target.UserName ?? string.Empty));
    }

    [HttpPost("stop")]
    [Authorize]
    [NoApiToken]
    public async Task<IActionResult> Stop(CancellationToken ct)
    {
        // Stop drops the cookie unconditionally — even if the principal happens to be
        // mid-impersonation right now, the middleware has already swapped User. The audit
        // row attributes back to the impersonating admin via the imp.admin claim.
        HttpContext.Response.Cookies.Delete(ImpersonationMiddleware.CookieName);

        string? rawAdminId = User.FindFirstValue(
            RequireOriginalIdentityAttribute.ImpersonatorClaimType
        );
        if (Guid.TryParse(rawAdminId, out Guid adminId))
        {
            string? rawTargetId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            try
            {
                await _audit.RecordAsync(
                    "auth.impersonate.stop",
                    "User",
                    rawTargetId ?? string.Empty,
                    new
                    {
                        adminUserId = adminId,
                        targetUserId = rawTargetId,
                        targetLogin = User.Identity?.Name,
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _log.LogBestEffortFailure(ex);
            }
        }
        return NoContent();
    }
}
