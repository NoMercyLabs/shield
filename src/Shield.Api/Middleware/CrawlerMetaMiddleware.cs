using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Shield.Api.Middleware;

// Intercepts unfurl-bot requests for SPA routes and serves an enriched index.html with
// route-tuned <title> / og:title / og:description. Regular browsers fall through to the
// existing SPA fallback unchanged.
//
// The static OG tags in index.html already give every URL a baseline preview without JS;
// this middleware exists so future per-route enrichment has a substrate to land in.
//
// HARD RULE: no DB lookup that returns user/repo/package/finding data is allowed here.
// Shield is private; the unfurl is public. Defaults only, plus a tiny safe route map.
public sealed class CrawlerMetaMiddleware : IMiddleware
{
    private static readonly Regex CrawlerUserAgent = new(
        @"Discordbot|Twitterbot|facebookexternalhit|Slackbot|LinkedInBot|WhatsApp|TelegramBot|Applebot|Googlebot|bingbot|redditbot|Mastodon|Bluesky",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CrawlerMetaMiddleware> _logger;

    public CrawlerMetaMiddleware(
        IWebHostEnvironment environment,
        ILogger<CrawlerMetaMiddleware> logger
    )
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldIntercept(context))
        {
            await next(context);
            return;
        }

        string userAgent = context.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(userAgent) || !CrawlerUserAgent.IsMatch(userAgent))
        {
            await next(context);
            return;
        }

        IFileInfo indexFile = _environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!indexFile.Exists)
        {
            await next(context);
            return;
        }

        string html;
        await using (Stream stream = indexFile.CreateReadStream())
        using (StreamReader reader = new(stream, Encoding.UTF8))
        {
            html = await reader.ReadToEndAsync(context.RequestAborted);
        }

        RouteMeta meta = ResolveMeta(context.Request.Path);
        string enriched = ApplyMeta(html, meta);

        _logger.LogInformation(
            "Crawler unfurl: ua={UserAgent} path={Path} title={Title}",
            userAgent,
            context.Request.Path.Value,
            meta.Title
        );

        ISecurityEventLogger? securityLog =
            context.RequestServices.GetService<ISecurityEventLogger>();
        if (securityLog is not null)
        {
            try
            {
                await securityLog.LogAsync(
                    source: "shield.crawler",
                    eventType: "crawler.detected",
                    severity: Shield.Core.Domain.Severity.Low,
                    remoteIp: context.Connection.RemoteIpAddress?.ToString(),
                    userAgent: userAgent,
                    path: context.Request.Path.Value,
                    ct: context.RequestAborted
                );
            }
            catch
            {
                // Crawler observation is informational — don't break the unfurl response.
            }
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(enriched, Encoding.UTF8, context.RequestAborted);
    }

    private static bool ShouldIntercept(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
            return false;
        PathString path = context.Request.Path;
        if (path.StartsWithSegments("/api"))
            return false;
        if (path.StartsWithSegments("/swagger"))
            return false;
        if (path.StartsWithSegments("/hubs"))
            return false;
        if (path.StartsWithSegments("/healthz"))
            return false;
        // Skip static-file requests (anything with a file extension other than .html).
        string? value = path.Value;
        if (value is not null)
        {
            int lastDot = value.LastIndexOf('.');
            int lastSlash = value.LastIndexOf('/');
            if (lastDot > lastSlash && !value.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static RouteMeta ResolveMeta(PathString path)
    {
        string normalised = (path.Value ?? "/").TrimEnd('/');
        if (string.IsNullOrEmpty(normalised))
            normalised = "/";

        return normalised switch
        {
            "/" or "/dashboard" => new(
                Title: "Shield",
                Description: "Self-hosted dependency vulnerability watcher.",
                Image: "/api/og/default.png"
            ),
            "/login" => new(
                Title: "Sign in to Shield",
                Description: "Sign in to your self-hosted Shield instance.",
                Image: "/api/og/default.png"
            ),
            "/accept-invite" => new(
                Title: "You've been invited to Shield",
                Description: "Accept your invite to a self-hosted Shield instance.",
                Image: "/api/og/default.png"
            ),
            _ => new(
                Title: "Shield",
                Description: "Self-hosted dependency vulnerability watcher.",
                Image: "/api/og/default.png"
            ),
        };
        // TODO: per-route enrichment hook. When a future surface exposes safe per-route
        // data (e.g. a public landing page for a published advisory, opt-in instance
        // statistics), wire it here behind a feature flag. Anything that requires a DB
        // lookup of user-owned data MUST NOT land in this map.
    }

    private static string ApplyMeta(string html, RouteMeta meta)
    {
        string updated = html;
        updated = ReplaceTitleTag(updated, meta.Title);
        updated = ReplaceMetaProperty(updated, "og:title", meta.Title);
        updated = ReplaceMetaProperty(updated, "og:description", meta.Description);
        updated = ReplaceMetaProperty(updated, "og:image", meta.Image);
        updated = ReplaceMetaName(updated, "description", meta.Description);
        updated = ReplaceMetaName(updated, "twitter:title", meta.Title);
        updated = ReplaceMetaName(updated, "twitter:description", meta.Description);
        updated = ReplaceMetaName(updated, "twitter:image", meta.Image);
        return updated;
    }

    private static string ReplaceTitleTag(string html, string title)
    {
        Regex pattern = new(@"<title>[^<]*</title>", RegexOptions.IgnoreCase);
        string replacement = $"<title>{EscapeHtml(title)}</title>";
        return pattern.IsMatch(html) ? pattern.Replace(html, replacement, count: 1) : html;
    }

    private static string ReplaceMetaProperty(string html, string property, string content)
    {
        Regex pattern = new(
            $"<meta[^>]*property=\"{Regex.Escape(property)}\"[^>]*/?>",
            RegexOptions.IgnoreCase
        );
        string replacement = $"<meta property=\"{property}\" content=\"{EscapeHtml(content)}\" />";
        return pattern.IsMatch(html) ? pattern.Replace(html, replacement, count: 1) : html;
    }

    private static string ReplaceMetaName(string html, string name, string content)
    {
        Regex pattern = new(
            $"<meta[^>]*name=\"{Regex.Escape(name)}\"[^>]*/?>",
            RegexOptions.IgnoreCase
        );
        string replacement = $"<meta name=\"{name}\" content=\"{EscapeHtml(content)}\" />";
        return pattern.IsMatch(html) ? pattern.Replace(html, replacement, count: 1) : html;
    }

    private static string EscapeHtml(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    private readonly record struct RouteMeta(string Title, string Description, string Image);
}
