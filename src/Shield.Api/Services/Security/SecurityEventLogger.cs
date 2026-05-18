using System.Net;
using Microsoft.AspNetCore.SignalR;
using Shield.Api.Hubs;

namespace Shield.Api.Services.Security;

// Writes a SecurityEvent + upserts the matching IpReputation rollup + broadcasts a
// `security.event` SignalR frame. The score-weighting table is intentionally simple
// and lives here, not in configuration: tuning happens via code review.
public sealed class SecurityEventLogger : ISecurityEventLogger
{
    private static readonly TimeSpan ReputationWindow = TimeSpan.FromDays(30);

    private readonly ShieldDbContext _db;
    private readonly IHubContext<FindingsHub> _hub;
    private readonly ILogger<SecurityEventLogger> _logger;

    public SecurityEventLogger(
        ShieldDbContext db,
        IHubContext<FindingsHub> hub,
        ILogger<SecurityEventLogger> logger
    )
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task LogAsync(SecurityEvent securityEvent, CancellationToken ct = default)
    {
        if (securityEvent.Id == Guid.Empty)
            securityEvent.Id = Guid.NewGuid();
        if (securityEvent.At == default)
            securityEvent.At = DateTime.UtcNow;
        securityEvent.RemoteIp = NormaliseIp(securityEvent.RemoteIp);

        _db.SecurityEvents.Add(securityEvent);

        if (!string.IsNullOrEmpty(securityEvent.RemoteIp))
            await UpsertReputationAsync(securityEvent, ct);

        await _db.SaveChangesAsync(ct);

        try
        {
            await _hub.Clients.All.SendAsync(
                "security.event",
                SecurityEventResponse.From(securityEvent),
                ct
            );
        }
        catch (Exception ex)
        {
            // SignalR broadcast failures must not roll back the persisted row.
            _logger.LogWarning(
                ex,
                "security.event SignalR broadcast failed for {EventId}",
                securityEvent.Id
            );
        }
    }

    public Task LogAsync(
        string source,
        string eventType,
        Severity severity,
        string? remoteIp = null,
        string? userAgent = null,
        string? userName = null,
        string? path = null,
        string? host = null,
        string? jail = null,
        string? detailsJson = null,
        CancellationToken ct = default
    ) =>
        LogAsync(
            new()
            {
                Id = Guid.NewGuid(),
                At = DateTime.UtcNow,
                Source = source,
                EventType = eventType,
                Severity = severity,
                RemoteIp = remoteIp,
                UserAgent = userAgent,
                UserName = userName,
                Path = path,
                Host = host,
                Jail = jail,
                DetailsJson = detailsJson,
            },
            ct
        );

    private async Task UpsertReputationAsync(SecurityEvent securityEvent, CancellationToken ct)
    {
        string ip = securityEvent.RemoteIp!;
        IpReputation? reputation = await _db.IpReputations.FirstOrDefaultAsync(
            row => row.Ip == ip,
            ct
        );

        DateTime now = securityEvent.At;
        int weight = WeightFor(securityEvent.Severity);

        if (reputation is null)
        {
            reputation = new()
            {
                Ip = ip,
                EventCount = 1,
                Score = weight,
                FirstSeenAt = now,
                LastSeenAt = now,
            };
            _db.IpReputations.Add(reputation);
        }
        else
        {
            // Slide the 30-day window: reset counters when the previous activity falls
            // outside the window so a long-quiet IP doesn't carry stale reputation.
            if (now - reputation.LastSeenAt > ReputationWindow)
            {
                reputation.EventCount = 0;
                reputation.Score = 0;
            }
            reputation.EventCount += 1;
            reputation.Score += weight;
            reputation.LastSeenAt = now;
        }

        // fail2ban-sourced events drive the CurrentlyBanned flag. Everything else is
        // observational and leaves the existing flag alone.
        if (securityEvent.Source == "fail2ban")
        {
            switch (securityEvent.EventType)
            {
                case "fail2ban.ban":
                    reputation.CurrentlyBanned = true;
                    reputation.LastBannedAt = now;
                    reputation.LastJail = securityEvent.Jail;
                    break;
                case "fail2ban.unban":
                    reputation.CurrentlyBanned = false;
                    reputation.LastUnbannedAt = now;
                    if (!string.IsNullOrEmpty(securityEvent.Jail))
                        reputation.LastJail = securityEvent.Jail;
                    break;
            }
        }
    }

    private static int WeightFor(Severity severity) =>
        severity switch
        {
            Severity.Critical => 20,
            Severity.High => 8,
            Severity.Medium => 3,
            _ => 1,
        };

    // ::ffff:1.2.3.4 → 1.2.3.4 so v4 + v4-mapped-v6 share one reputation row.
    private static string? NormaliseIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;
        if (!IPAddress.TryParse(ip, out IPAddress? parsed))
            return ip;
        if (parsed.IsIPv4MappedToIPv6)
            return parsed.MapToIPv4().ToString();
        return parsed.ToString();
    }
}
