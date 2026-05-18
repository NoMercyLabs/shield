using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Middleware;

// Reads the shield.impersonate cookie and, when the AUTHENTICATED CALLER is an Admin, swaps
// HttpContext.User to a synthetic principal carrying the impersonated user's id + roles +
// username, plus a custom `imp.admin` claim holding the original admin's id.
//
// Runs AFTER UseAuthentication so context.User reflects the real cookie/JWT/SingleUser
// principal we trust to gate the override on. Runs BEFORE UseAuthorization (and the 2FA gate,
// the audit logger, MapControllers) so downstream policies and the IAccessResolver see the
// SWAPPED principal — that's the entire point: the admin lives the impersonated user's view.
//
// Invariants enforced here (never trust the cookie alone):
//   1. The pre-swap principal MUST be in the Admin role; otherwise we drop the cookie and
//      proceed without override. A non-admin stealing the cookie can't escalate.
//   2. The cookie's AdminUserId MUST match the pre-swap principal's nameidentifier. Stops
//      cross-admin replay if an admin somehow lifted another admin's encrypted blob.
//   3. The impersonated user MUST exist and MUST NOT be in the Admin role. Admin-on-admin
//      impersonation is rejected at /start; this is the second line of defence in case the
//      target was demoted before the cookie expired.
//   4. Cookie payload past MaxLifetime → silently ignored; admin sees their normal view next
//      request without an explicit /stop.
public sealed class ImpersonationMiddleware : IMiddleware
{
    public const string CookieName = "shield.impersonate";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (
            !context.Request.Cookies.TryGetValue(CookieName, out string? cookieValue)
            || string.IsNullOrWhiteSpace(cookieValue)
        )
        {
            await next(context);
            return;
        }

        // No authenticated principal → there's nothing to gate the swap on; drop the cookie
        // so a logged-out browser can't keep pretending it's mid-impersonation.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        if (!context.User.IsInRole(ShieldRoles.Admin))
        {
            // Authenticated, but not as Admin. Could be the admin's session after a role
            // change, or a stolen cookie pasted into a viewer's browser. Either way, drop.
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        IImpersonationCookieIssuer issuer =
            context.RequestServices.GetRequiredService<IImpersonationCookieIssuer>();
        ImpersonationPayload? payload = issuer.Unprotect(cookieValue);
        if (payload is null)
        {
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        // Caller's nameidentifier must match the cookie's bound admin id.
        string? rawCallerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawCallerId, out Guid callerId) || callerId != payload.AdminUserId)
        {
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        UserManager<ShieldUser> userManager = context.RequestServices.GetRequiredService<
            UserManager<ShieldUser>
        >();
        SignInManager<ShieldUser> signInManager = context.RequestServices.GetRequiredService<
            SignInManager<ShieldUser>
        >();
        ShieldUser? target = await userManager.FindByIdAsync(payload.ImpersonatedUserId.ToString());
        if (target is null)
        {
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        IList<string> targetRoles = await userManager.GetRolesAsync(target);
        if (targetRoles.Contains(ShieldRoles.Admin))
        {
            // Admin-on-admin impersonation is denied at /start, but the target may have been
            // PROMOTED after the cookie was minted. Hard-stop here.
            context.Response.Cookies.Delete(CookieName);
            await next(context);
            return;
        }

        // Build the swapped principal off the same factory Identity uses for normal sign-ins
        // so the claim types (UserIdClaimType, RoleClaimType, etc.) match what
        // UserManager.GetUserAsync / IsInRole expect. Adding the imp.admin* claims afterwards
        // preserves the admin's seat for audit + the SPA banner.
        ClaimsPrincipal targetPrincipal = await signInManager.CreateUserPrincipalAsync(target);
        if (targetPrincipal.Identity is ClaimsIdentity primaryIdentity)
        {
            primaryIdentity.AddClaim(
                new(
                    RequireOriginalIdentityAttribute.ImpersonatorClaimType,
                    payload.AdminUserId.ToString()
                )
            );
            string? originalAdminName = context.User.Identity?.Name;
            if (!string.IsNullOrEmpty(originalAdminName))
                primaryIdentity.AddClaim(new("imp.admin.name", originalAdminName));
        }
        context.User = targetPrincipal;
        await next(context);
    }
}
