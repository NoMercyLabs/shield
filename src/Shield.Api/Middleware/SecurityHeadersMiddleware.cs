namespace Shield.Api.Middleware;

// Stamps the response with a hardened header set. Lives early in the pipeline so static
// files, SPA fallback, controllers, and SignalR all get the same headers. HSTS is only
// emitted when RequireHttps is true because shipping it to an http:// origin pins the
// browser to a TLS endpoint that doesn't exist yet.
//
// CSP exceptions cover the four outbound services Shield needs from the browser:
//   - api.osv.dev: OSV vulnerability lookups for the inventory diff modal
//   - api.github.com: repo metadata + advisory enrichment for findings detail
//   - discord.com: Discord channel test-send button hits the webhook directly from the SPA
//   - api.slack.com: Slack channel test-send button
// SignalR runs same-origin over wss: when TLS is on (covered by `connect-src wss:`).
public sealed class SecurityHeadersMiddleware : IMiddleware
{
    private const string ContentSecurityPolicyValue =
        "default-src 'self'; "
        + "img-src 'self' data:; "
        + "style-src 'self' 'unsafe-inline'; "
        + "script-src 'self'; "
        + "connect-src 'self' wss: https://api.osv.dev https://api.github.com https://discord.com https://api.slack.com;";

    private readonly bool _requireHttps;

    public SecurityHeadersMiddleware(IConfiguration configuration)
    {
        _requireHttps = configuration.GetValue("Shield:Auth:RequireHttps", false);
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        IHeaderDictionary headers = context.Response.Headers;

        // OnStarting fires right before headers flush so we never collide with static-file
        // middleware that sets its own headers earlier in the pipeline.
        context.Response.OnStarting(() =>
        {
            if (_requireHttps)
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

            headers["Content-Security-Policy"] = ContentSecurityPolicyValue;
            headers["X-Frame-Options"] = "DENY";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            return Task.CompletedTask;
        });

        return next(context);
    }
}
