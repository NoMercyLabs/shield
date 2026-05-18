using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shield.Api.Services;
using Shield.Core.Options;

namespace Shield.Api.Middleware;

// When `auth.require_2fa` is true, blocks API calls from authenticated users whose Identity
// flag `TwoFactorEnabled` is false. Returns 403 with a discoverable body so the SPA can route
// the user to the enrollment page. Lets anonymous + the synthetic single-user admin + the
// 2FA-enrollment endpoints through so users can self-rescue.
public sealed class TwoFactorEnforcementMiddleware : IMiddleware
{
    private static readonly string[] s_allowedPathPrefixes = ["/api/auth/2fa/"];

    private static readonly HashSet<string> s_allowedExactPaths = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "/api/auth/me",
        "/api/auth/logout",
        "/api/auth/2fa/enroll",
        "/api/auth/2fa/verify",
        "/api/auth/2fa/recovery",
    };

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        string path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (context.User.Identity is not { IsAuthenticated: true })
        {
            await next(context);
            return;
        }

        if (IsAllowedPath(path))
        {
            await next(context);
            return;
        }

        ITwoFactorEnforcement enforcement =
            context.RequestServices.GetRequiredService<ITwoFactorEnforcement>();
        bool required = await enforcement.IsRequiredAsync(context.RequestAborted);
        if (!required)
        {
            await next(context);
            return;
        }

        // The synthetic single-user admin can't enroll itself (auto-auth handler short-circuits
        // the cookie pipeline), so the policy can't apply to it. Real admins enrolling under
        // their own Identity row still hit the check.
        IOptions<ShieldOptions> shieldOptions = context.RequestServices.GetRequiredService<
            IOptions<ShieldOptions>
        >();
        if (
            shieldOptions.Value.SingleUser
            && string.Equals(
                context.User.Identity.Name,
                IdentitySeeder.SingleUserName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            await next(context);
            return;
        }

        UserManager<ShieldUser> userManager = context.RequestServices.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldUser? user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            await next(context);
            return;
        }

        if (user.TwoFactorEnabled)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        string body = JsonSerializer.Serialize(
            new { code = "two_factor_required", enrollUrl = "/account/2fa" }
        );
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static bool IsAllowedPath(string path)
    {
        if (s_allowedExactPaths.Contains(path))
            return true;
        foreach (string prefix in s_allowedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
