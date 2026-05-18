using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Middleware;

// Reads the opaque shield.session cookie and:
//   - if absent → no-op (some clients use JWT bearer or the SingleUser convenience scheme)
//   - if present + matches a non-revoked row → write-coalesced LastActiveAt touch
//   - if present + row missing or revoked → clear cookie + 401 (only for /api requests)
// Stores the resolved UserSession on HttpContext.Items["UserSession"] so SessionsController
// can mark the current row in /api/sessions.
public sealed class SessionTrackingMiddleware : IMiddleware
{
    public const string CookieName = "shield.session";
    public const string ContextItemKey = "shield.session.row";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        bool isApi = context.Request.Path.StartsWithSegments("/api");
        bool hasCookie =
            context.Request.Cookies.TryGetValue(CookieName, out string? opaqueToken)
            && !string.IsNullOrWhiteSpace(opaqueToken);
        // SingleUserAuthHandler builds its principal via SignInManager.CreateUserPrincipalAsync,
        // which tags the inner identity with AuthenticationType=IdentityConstants.ApplicationScheme
        // — same as a real cookie. The handler stamps a marker claim so we can tell them apart.
        bool isSingleUserPrincipal = context.User.HasClaim(c =>
            c.Type == SingleUserAuthHandler.SingleUserClaimType
        );
        bool isCookieAuth =
            !isSingleUserPrincipal
            && context.User.Identity?.IsAuthenticated == true
            && context.User.Identities.Any(id =>
                id.AuthenticationType == IdentityConstants.ApplicationScheme
            );

        if (!hasCookie)
        {
            // Cookie-auth without our session cookie = browser kept the Identity cookie after
            // a revoke-induced clear. Force a re-login by signing out + 401 (api only).
            // Bearer + SingleUser principals don't carry the application cookie so they pass.
            if (isApi && isCookieAuth)
            {
                await SignOutAndRejectAsync(context);
                return;
            }
            await next(context);
            return;
        }

        ISessionTracker tracker = context.RequestServices.GetRequiredService<ISessionTracker>();
        UserSession? session = await tracker.FindByOpaqueTokenAsync(
            opaqueToken!,
            context.RequestAborted
        );

        if (session is null || session.RevokedAt is not null)
        {
            // Revoked-cookie replay attempts are the most interesting session-layer signal:
            // a cookie that USED to be valid being replayed after revoke means a session
            // token leaked or the user lost a device. High severity — operators may want
            // a fail2ban jail on this if it sustains.
            ISecurityEventLogger? securityLog =
                context.RequestServices.GetService<ISecurityEventLogger>();
            if (securityLog is not null)
            {
                try
                {
                    await securityLog.LogAsync(
                        source: "shield.auth",
                        eventType: "session.replay",
                        severity: Severity.High,
                        remoteIp: context.Connection.RemoteIpAddress?.ToString(),
                        userAgent: context.Request.Headers.UserAgent.ToString()
                            is { Length: > 0 } ua
                            ? ua
                            : null,
                        userName: session?.UserId.ToString(),
                        path: context.Request.Path.Value,
                        ct: context.RequestAborted
                    );
                }
                catch
                {
                    // Best-effort.
                }
            }

            context.Response.Cookies.Delete(CookieName);
            if (isApi)
            {
                await SignOutAndRejectAsync(context);
                return;
            }
            await next(context);
            return;
        }

        context.Items[ContextItemKey] = session;
        await tracker.TouchAsync(session, context.RequestAborted);

        await next(context);
    }

    private static async Task SignOutAndRejectAsync(HttpContext context)
    {
        SignInManager<ShieldUser> signInManager = context.RequestServices.GetRequiredService<
            SignInManager<ShieldUser>
        >();
        await signInManager.SignOutAsync();
        // SignOutAsync also clears IdentityConstants.ApplicationScheme cookie + ExternalScheme +
        // TwoFactorRememberMeScheme. Belt+braces: explicitly clear our session cookie too.
        context.Response.Cookies.Delete(CookieName);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
