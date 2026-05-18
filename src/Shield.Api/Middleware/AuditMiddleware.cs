using System.Text.RegularExpressions;

namespace Shield.Api.Middleware;

// Inspects each request's path + verb and, on 2xx, writes an AuditEntry naming the action
// (finding.ack, source.update, …) plus the parsed target id. Whitelist-only so day-to-day
// reads (GET /api/findings, etc.) don't fill the table; controllers are not touched.
public sealed class AuditMiddleware : IMiddleware
{
    private static readonly Regex s_findingTransition = new(
        @"^/api/findings/(?<id>[0-9a-f-]{36})/(?<verb>ack|resolve|suppress)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex s_findingBulk = new(
        @"^/api/findings/bulk-(?<verb>ack|resolve|suppress)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex s_sourceById = new(
        @"^/api/sources/(?<id>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex s_channelById = new(
        @"^/api/channels/(?<id>[0-9a-f-]{36})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex s_oauthDisconnect = new(
        @"^/api/oauth/(?<provider>[A-Za-z]+)/disconnect$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex s_oauthCallback = new(
        @"^/api/oauth/(?<provider>[A-Za-z]+)/callback$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

        int status = context.Response.StatusCode;
        // Audit on success (2xx) AND on OAuth callback redirects (302) — the latter signals
        // a successful provider handshake; failures land on a different redirect path.
        bool isSuccess = status is >= 200 and < 300;
        bool isOAuthRedirect = status is 301 or 302;
        if (!isSuccess && !isOAuthRedirect)
            return;

        string path = context.Request.Path.Value ?? string.Empty;
        string method = context.Request.Method;

        (string? action, string? targetType, string? targetId) = Classify(method, path, status);
        if (action is null || targetType is null || targetId is null)
            return;

        IAuditLogger logger = context.RequestServices.GetRequiredService<IAuditLogger>();
        try
        {
            await logger.RecordAsync(
                action,
                targetType,
                targetId,
                details: new
                {
                    method,
                    path,
                    status,
                },
                ct: context.RequestAborted
            );
        }
        catch
        {
            // Audit-log failures must not break the user-facing response. Swallow silently
            // and rely on host logging if persistence is broken (a separate alarm).
        }
    }

    private static (string? Action, string? TargetType, string? TargetId) Classify(
        string method,
        string path,
        int status
    )
    {
        Match findingTransition = s_findingTransition.Match(path);
        if (findingTransition.Success && method == "POST")
        {
            string verb = findingTransition.Groups["verb"].Value.ToLowerInvariant();
            return ($"finding.{verb}", "Finding", findingTransition.Groups["id"].Value);
        }

        Match findingBulk = s_findingBulk.Match(path);
        if (findingBulk.Success && method == "POST")
        {
            string verb = findingBulk.Groups["verb"].Value.ToLowerInvariant();
            return ($"finding.bulk-{verb}", "Finding", "bulk");
        }

        if (
            method == "POST"
            && string.Equals(path, "/api/sources", StringComparison.OrdinalIgnoreCase)
        )
            return ("source.create", "Source", "(new)");

        if (
            method == "POST"
            && path.StartsWith("/api/sources/bulk-", StringComparison.OrdinalIgnoreCase)
        )
            return ("source.bulk-create", "Source", "bulk");

        Match sourceById = s_sourceById.Match(path);
        if (sourceById.Success)
        {
            return method switch
            {
                "PUT" => ("source.update", "Source", sourceById.Groups["id"].Value),
                "DELETE" => ("source.delete", "Source", sourceById.Groups["id"].Value),
                _ => (null, null, null),
            };
        }

        if (
            method == "POST"
            && string.Equals(path, "/api/channels", StringComparison.OrdinalIgnoreCase)
        )
            return ("channel.create", "Channel", "(new)");

        Match channelById = s_channelById.Match(path);
        if (channelById.Success)
        {
            return method switch
            {
                "PUT" => ("channel.update", "Channel", channelById.Groups["id"].Value),
                "DELETE" => ("channel.delete", "Channel", channelById.Groups["id"].Value),
                _ => (null, null, null),
            };
        }

        if (
            method == "PUT"
            && string.Equals(path, "/api/settings", StringComparison.OrdinalIgnoreCase)
        )
            return ("settings.update", "Setting", "shield");

        Match oauthDisconnect = s_oauthDisconnect.Match(path);
        if (oauthDisconnect.Success && method == "POST")
            return (
                "oauth.disconnect",
                "OAuth",
                oauthDisconnect.Groups["provider"].Value.ToLowerInvariant()
            );

        Match oauthCallback = s_oauthCallback.Match(path);
        if (oauthCallback.Success && method == "GET" && status is 200 or 301 or 302)
            return (
                "oauth.connect",
                "OAuth",
                oauthCallback.Groups["provider"].Value.ToLowerInvariant()
            );

        return (null, null, null);
    }
}
