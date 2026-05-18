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
//
// Operators can extend connect-src via Shield:Security:Csp:ExtraConnectSrc (CSV) when their
// deploy needs additional egress endpoints (metrics collector, telemetry sink, etc.).
public sealed class SecurityHeadersMiddleware : IMiddleware
{
    private const string BaseConnectSrc =
        "'self' wss: https://api.osv.dev https://api.github.com https://discord.com https://api.slack.com";

    private readonly bool _requireHttps;
    private readonly bool _isPublic;
    private readonly string _contentSecurityPolicyValue;

    public SecurityHeadersMiddleware(IConfiguration configuration)
    {
        _requireHttps = configuration.GetValue("Shield:Auth:RequireHttps", false);
        _isPublic = configuration.GetValue("Shield:Public", false);

        string extraConnect = JoinCsv(configuration["Shield:Security:Csp:ExtraConnectSrc"]);
        string extraScript = JoinCsv(configuration["Shield:Security:Csp:ExtraScriptSrc"]);
        string extraImg = JoinCsv(configuration["Shield:Security:Csp:ExtraImgSrc"]);

        // Cloudflare Tunnel auto-injects a beacon script (static.cloudflareinsights.com) and a
        // sha256 inline tag. Operators behind cloudflared opt-in via Shield:Security:Csp:CloudflareBeacon.
        string cloudflarePieces = string.Empty;
        if (configuration.GetValue("Shield:Security:Csp:CloudflareBeacon", false))
        {
            cloudflarePieces =
                " https://static.cloudflareinsights.com "
                // Cloudflare's bootstrap is a small inline <script> that the page-rules engine
                // stamps in. The browser surfaces its hash on first block; this is the value
                // that has been stable across cloudflared releases — refresh from the console
                // error if Cloudflare rotates the script body.
                + "'sha256-qBMtfGEJZwFUzfr5orJyQhwAQaVDQWPGTOWk4BivbLk='";
        }

        // upgrade-insecure-requests only emits when TLS is on — sending it to http:// dev
        // origins forces the browser to upgrade asset URLs to https:// that don't exist.
        string upgradeDirective = _requireHttps ? " upgrade-insecure-requests;" : string.Empty;

        _contentSecurityPolicyValue =
            "default-src 'self'; "
            + $"img-src 'self' data: https://avatars.githubusercontent.com https://github.githubassets.com https://a.slack-edge.com https://www.google.com{extraImg}; "
            // style-src 'unsafe-inline' is a v0.1 limitation — Tailwind compiles fine but
            // Vue components use :style bindings that need inline. Tracking issue: move to
            // nonce-based CSS once we rip out the last inline :style.
            + "style-src 'self' 'unsafe-inline'; "
            + $"script-src 'self'{cloudflarePieces}{extraScript}; "
            + "worker-src 'self'; "
            + "object-src 'none'; "
            + "base-uri 'self'; "
            + "form-action 'self'; "
            + "frame-ancestors 'none'; "
            + $"connect-src {BaseConnectSrc}{extraConnect};"
            + upgradeDirective;
    }

    private static string JoinCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return string.Empty;
        string[] entries = csv.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return entries.Length == 0 ? string.Empty : " " + string.Join(' ', entries);
    }

    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        IHeaderDictionary headers = context.Response.Headers;

        // OnStarting fires right before headers flush so we never collide with static-file
        // middleware that sets its own headers earlier in the pipeline.
        context.Response.OnStarting(() =>
        {
            if (_requireHttps)
            {
                // Belt-and-braces: UseHsts only emits on https requests, but the middleware
                // sees every request — emitting unconditionally when RequireHttps is on means
                // a misconfigured proxy that drops the header still lands the directive at
                // the browser.
                string hstsValue = _isPublic
                    ? "max-age=31536000; includeSubDomains"
                    : "max-age=2592000";
                headers["Strict-Transport-Security"] = hstsValue;
            }

            headers["Content-Security-Policy"] = _contentSecurityPolicyValue;
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
