using Shield.Api.Middleware;

namespace Shield.Api.Auth;

// Single source of truth for the shield.session cookie. Four signin paths (password login,
// invite acceptance, OAuth signin callback, external-login device-flow poll) issue this cookie
// and they USED to inline the same 25-line block four times. Drift between copies caused the
// "sign out other devices" leak: one path issued the cookie, another forgot, the Identity
// cookie alone kept that browser authenticated. The shared issuer guarantees parity.
public interface ISessionCookieIssuer
{
    // Mints a UserSession row + sets shield.session on the response. Returns the row so the
    // caller can audit / notify with the new session id. Reads UA + IP from HttpContext.
    Task<UserSession> IssueAsync(HttpContext httpContext, Guid userId, CancellationToken ct);
}

public sealed class SessionCookieIssuer : ISessionCookieIssuer
{
    private readonly ISessionTracker _sessionTracker;

    public SessionCookieIssuer(ISessionTracker sessionTracker)
    {
        _sessionTracker = sessionTracker;
    }

    public async Task<UserSession> IssueAsync(
        HttpContext httpContext,
        Guid userId,
        CancellationToken ct
    )
    {
        string? userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = null;
        string? remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        (UserSession session, string opaqueToken) = await _sessionTracker.CreateAsync(
            userId,
            userAgent,
            remoteIp,
            ct
        );
        bool requireHttps = httpContext.Request.IsHttps;
        httpContext.Response.Cookies.Append(
            SessionTrackingMiddleware.CookieName,
            opaqueToken,
            new()
            {
                HttpOnly = true,
                Secure = requireHttps,
                SameSite = requireHttps ? SameSiteMode.Strict : SameSiteMode.Lax,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/",
            }
        );
        return session;
    }
}
