using System.Net;
using Microsoft.Extensions.Primitives;
// Microsoft.AspNetCore.HttpOverrides ships its own legacy IPNetwork that shadows the
// System.Net one we use below. Alias to keep the right type resolving.
using IPNetwork = System.Net.IPNetwork;

namespace Shield.Api.Hardening;

// Real-client-IP extractor. Walks forwarded-IP headers in priority order and picks the
// first PUBLIC IP it finds — same shape as nomercy-tv's getIp() Laravel helper. No
// peer-trust check: any topology where the operator binds Shield to loopback or a
// private network (which is every supported deployment) means a public IP arriving in
// any of these headers came through the proxy chain authoritatively. A client that
// could reach Shield directly with a public source IP wouldn't need to forge the
// header to attack — the bigger problem is the exposure itself.
//
// Header priority — CF-specific first because CF-Connecting-IP / -IPv6 are single
// authoritative values (no chain), then the generic forwarded headers as fallback for
// non-Cloudflare deployments.
public sealed class CloudflareForwardedIpMiddleware
{
    public const string VisitorHeader = "CF-Visitor";

    private static readonly string[] ClientIpHeaders =
    [
        "CF-Connecting-IPv6",
        "CF-Connecting-IP",
        "True-Client-IP",
        "X-Real-IP",
        "X-Client-IP",
        "X-Forwarded-For",
        "Forwarded-For",
        "Forwarded",
    ];

    private readonly RequestDelegate _next;

    public CloudflareForwardedIpMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        IPAddress? clientIp = ExtractPublicClientIp(context.Request.Headers);
        if (clientIp is not null)
            context.Connection.RemoteIpAddress = clientIp;

        // CF-Visitor: {"scheme":"https"} when CF terminated TLS. Honour it so Request.Scheme
        // reports https even if the origin connection is plain HTTP — otherwise
        // HttpsRedirection loops between proxy and app.
        if (
            context.Request.Headers.TryGetValue(VisitorHeader, out StringValues cfVisitor)
            && cfVisitor.Count > 0
        )
        {
            string raw = cfVisitor[0] ?? string.Empty;
            if (raw.Contains("\"scheme\":\"https\"", StringComparison.OrdinalIgnoreCase))
                context.Request.Scheme = "https";
        }

        return _next(context);
    }

    private static IPAddress? ExtractPublicClientIp(IHeaderDictionary headers)
    {
        foreach (string headerName in ClientIpHeaders)
        {
            if (!headers.TryGetValue(headerName, out StringValues values))
                continue;
            foreach (string? raw in values)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                // X-Forwarded-For / Forwarded-For etc. can be comma-separated. CF-Connecting-IP
                // is single-valued but the split is harmless. Take the first public IP we see.
                foreach (string candidate in raw.Split(','))
                {
                    string trimmed = candidate.Trim();
                    // Strip an optional [::1]:8080 / 1.2.3.4:8080 port suffix some proxies add.
                    trimmed = StripPort(trimmed);
                    if (
                        IPAddress.TryParse(trimmed, out IPAddress? parsed)
                        && IsPublicAddress(parsed)
                    )
                        return parsed;
                }
            }
        }
        return null;
    }

    private static string StripPort(string raw)
    {
        // Bracketed IPv6 with port: "[::1]:443" → "::1"
        if (raw.StartsWith('['))
        {
            int close = raw.IndexOf(']');
            return close > 0 ? raw.Substring(1, close - 1) : raw;
        }
        // Plain IPv4 with port: "1.2.3.4:443" → "1.2.3.4". Bare IPv6 has multiple ':'
        // and can't carry a port without brackets — leave those alone.
        int firstColon = raw.IndexOf(':');
        int lastColon = raw.LastIndexOf(':');
        if (firstColon == lastColon && firstColon > 0)
            return raw[..firstColon];
        return raw;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        IPAddress ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(ip))
            return false;
        foreach (IPNetwork network in NonPublicRanges)
        {
            if (network.Contains(ip))
                return false;
        }
        return true;
    }

    // Everything FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE rejects in PHP,
    // expressed as CIDRs so we can match against a parsed IPAddress.
    private static readonly IPNetwork[] NonPublicRanges =
    [
        // IPv4 — RFC1918 private
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
        // IPv4 — link-local, multicast, reserved
        new(IPAddress.Parse("169.254.0.0"), 16),
        new(IPAddress.Parse("224.0.0.0"), 4),
        new(IPAddress.Parse("0.0.0.0"), 8),
        new(IPAddress.Parse("240.0.0.0"), 4),
        // IPv4 — CGNAT (RFC6598) — shared address space, not internet-routable
        new(IPAddress.Parse("100.64.0.0"), 10),
        // IPv4 — documentation ranges (RFC5737)
        new(IPAddress.Parse("192.0.2.0"), 24),
        new(IPAddress.Parse("198.51.100.0"), 24),
        new(IPAddress.Parse("203.0.113.0"), 24),
        // IPv6 — unique-local, link-local, multicast
        new(IPAddress.Parse("fc00::"), 7),
        new(IPAddress.Parse("fe80::"), 10),
        new(IPAddress.Parse("ff00::"), 8),
        // IPv6 — documentation (RFC3849)
        new(IPAddress.Parse("2001:db8::"), 32),
    ];
}
