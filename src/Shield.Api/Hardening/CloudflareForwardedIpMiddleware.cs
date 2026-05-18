using System.Net;
using Microsoft.Extensions.Options;

namespace Shield.Api.Hardening;

// Cloudflare-aware reverse-proxy unwrap. The stock ForwardedHeaders middleware uses
// X-Forwarded-For, which works behind any proxy whose IP we've added to KnownNetworks.
// Behind Cloudflare specifically, CF-Connecting-IP is the better signal: it's a single
// authoritative client IP (no chain to parse, can't be padded by an earlier hop) and
// Cloudflare sets it on every request through their edge.
//
// We only honour the header when the immediate peer is from Cloudflare's published IP
// range — a client connecting directly to the origin and forging the header is otherwise
// trivial. Ranges default to Cloudflare's public list and are configurable via
// `Shield:Cloudflare:IpRanges` for the rare case CF announces new prefixes faster than
// Shield ships a release.
public sealed class CloudflareForwardedIpMiddleware
{
    public const string ConnectingIpHeader = "CF-Connecting-IP";
    public const string VisitorHeader = "CF-Visitor";

    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<IPNetwork> _cloudflareRanges;

    public CloudflareForwardedIpMiddleware(
        RequestDelegate next,
        IOptions<CloudflareForwardedIpOptions> options
    )
    {
        _next = next;
        _cloudflareRanges = options.Value.ResolveRanges();
    }

    public Task InvokeAsync(HttpContext context)
    {
        IPAddress? peer = context.Connection.RemoteIpAddress;
        if (peer is not null && IsCloudflarePeer(peer))
        {
            if (
                context.Request.Headers.TryGetValue(
                    ConnectingIpHeader,
                    out Microsoft.Extensions.Primitives.StringValues cfIp
                )
                && cfIp.Count > 0
                && IPAddress.TryParse(cfIp[0], out IPAddress? realClient)
            )
            {
                context.Connection.RemoteIpAddress = realClient;
            }

            // CF-Visitor: {"scheme":"https"} on TLS terminated by Cloudflare. ASP.NET Core's
            // ForwardedHeaders middleware reads X-Forwarded-Proto separately; honouring CF-
            // Visitor here covers the case where the origin connection itself isn't TLS but
            // the client-side was, which would otherwise make Request.Scheme report "http"
            // and trip HttpsRedirection into a loop.
            if (
                context.Request.Headers.TryGetValue(
                    VisitorHeader,
                    out Microsoft.Extensions.Primitives.StringValues cfVisitor
                )
                && cfVisitor.Count > 0
            )
            {
                string raw = cfVisitor[0] ?? string.Empty;
                if (raw.Contains("\"scheme\":\"https\"", StringComparison.OrdinalIgnoreCase))
                    context.Request.Scheme = "https";
            }
        }

        return _next(context);
    }

    private bool IsCloudflarePeer(IPAddress peer)
    {
        foreach (IPNetwork network in _cloudflareRanges)
        {
            if (network.Contains(peer))
                return true;
        }
        return false;
    }
}

public sealed class CloudflareForwardedIpOptions
{
    public const string SectionName = "Shield:Cloudflare";

    // Operator can override the embedded defaults via configuration in case Cloudflare
    // announces new prefixes between Shield releases. Format: comma-separated CIDR list.
    public string? IpRanges { get; set; }

    // Embedded Cloudflare public IP ranges — current as of 2026-05-18. Pulled from the
    // canonical lists at https://www.cloudflare.com/ips-v4 and /ips-v6.
    private static readonly string[] DefaultRangesV4 =
    [
        "173.245.48.0/20",
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "141.101.64.0/18",
        "108.162.192.0/18",
        "190.93.240.0/20",
        "188.114.96.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
        "162.158.0.0/15",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "172.64.0.0/13",
        "131.0.72.0/22",
    ];

    private static readonly string[] DefaultRangesV6 =
    [
        "2400:cb00::/32",
        "2606:4700::/32",
        "2803:f800::/32",
        "2405:b500::/32",
        "2405:8100::/32",
        "2a06:98c0::/29",
        "2c0f:f248::/32",
    ];

    public IReadOnlyList<IPNetwork> ResolveRanges()
    {
        IEnumerable<string> raw = !string.IsNullOrWhiteSpace(IpRanges)
            ? IpRanges.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            : DefaultRangesV4.Concat(DefaultRangesV6);

        List<IPNetwork> ranges = [];
        foreach (string entry in raw)
        {
            int slash = entry.IndexOf('/');
            if (slash <= 0)
                continue;
            string addr = entry[..slash];
            string prefix = entry[(slash + 1)..];
            if (
                IPAddress.TryParse(addr, out IPAddress? parsed)
                && int.TryParse(prefix, out int prefixLength)
            )
            {
                ranges.Add(new(parsed, prefixLength));
            }
        }
        return ranges;
    }
}
