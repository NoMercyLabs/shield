using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Middleware;

// Reads the opaque shield.session cookie and:
//   - if absent → no-op (some clients use JWT bearer)
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
        bool isCookieAuth =
            context.User.Identity?.IsAuthenticated == true
            && context.User.Identities.Any(id =>
                id.AuthenticationType == IdentityConstants.ApplicationScheme
            );

        if (!hasCookie)
        {
            // Cookie-auth without our session cookie = browser kept the Identity cookie after
            // a revoke-induced clear. Force a re-login by signing out + 401 (api only).
            // Bearer principals don't carry the application cookie so they pass.
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
            // Two failure modes, very different signal:
            //   - Session row EXISTS but is revoked → a cookie that USED to be valid is
            //     being replayed after revoke. Real session-theft signal. High severity,
            //     fail2ban-jail-worthy if it sustains.
            //   - Session row is MISSING → the cookie outlived its row. Happens after an
            //     admin wipe / session-table prune / long-expired cookie. Operator action,
            //     not an attack. Log it Low so the Security view doesn't fill with noise.
            bool isRevokedReplay = session is not null && session.RevokedAt is not null;
            ISecurityEventLogger? securityLog =
                context.RequestServices.GetService<ISecurityEventLogger>();
            if (securityLog is not null)
            {
                try
                {
                    await securityLog.LogAsync(
                        source: "shield.auth",
                        eventType: isRevokedReplay
                            ? "session.revoked_cookie_replay"
                            : "session.stale_cookie_presented",
                        severity: isRevokedReplay ? Severity.High : Severity.Low,
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
