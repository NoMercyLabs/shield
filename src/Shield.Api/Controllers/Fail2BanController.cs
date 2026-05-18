using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

// Ingestion endpoint hit by every configured fail2ban host. Auth is a single shared secret
// (`Shield:Security:Fail2BanIngestKey`) checked with FixedTimeEquals so an attacker can't
// time-shave the comparison. Rate-limited by the `fail2ban-ingest` policy — bursts during
// an attack are expected, but a single host should never sustain more than ~10/s.
[ApiController]
[Route("api/security/fail2ban")]
[AllowAnonymous]
[EnableRateLimiting("fail2ban-ingest")]
public sealed class Fail2BanController : ControllerBase
{
    public const string IngestKeyHeader = "X-Shield-Fail2Ban-Key";

    private readonly ISecurityEventLogger _securityLog;
    private readonly INotificationPublisher _notifications;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Fail2BanController> _logger;

    public Fail2BanController(
        ISecurityEventLogger securityLog,
        INotificationPublisher notifications,
        IConfiguration configuration,
        ILogger<Fail2BanController> logger
    )
    {
        _securityLog = securityLog;
        _notifications = notifications;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("event")]
    public async Task<IActionResult> Ingest(
        [FromBody] Fail2BanIngestRequest request,
        CancellationToken ct
    )
    {
        string? configuredKey = _configuration["Shield:Security:Fail2BanIngestKey"];
        if (string.IsNullOrEmpty(configuredKey))
        {
            _logger.LogWarning(
                "fail2ban ingest attempted but Shield:Security:Fail2BanIngestKey is not configured"
            );
            return Unauthorized();
        }

        string presented = Request.Headers[IngestKeyHeader].ToString();
        if (string.IsNullOrEmpty(presented) || !ConstantTimeEquals(presented, configuredKey))
            return Unauthorized();

        if (
            string.IsNullOrWhiteSpace(request.Host)
            || string.IsNullOrWhiteSpace(request.Jail)
            || string.IsNullOrWhiteSpace(request.Ip)
            || string.IsNullOrWhiteSpace(request.EventType)
        )
            return BadRequest(new { error = "host, jail, ip, and eventType are required." });

        string normalisedEventType = request.EventType.ToLowerInvariant() switch
        {
            "ban" => "fail2ban.ban",
            "unban" => "fail2ban.unban",
            "found" => "fail2ban.found",
            _ => $"fail2ban.{request.EventType.ToLowerInvariant()}",
        };

        Severity severity = normalisedEventType switch
        {
            "fail2ban.ban" => Severity.High,
            "fail2ban.unban" => Severity.Low,
            _ => Severity.Medium,
        };

        string? detailsJson = null;
        if (request.Matches is { Length: > 0 })
        {
            detailsJson = JsonSerializer.Serialize(
                new { matches = request.Matches },
                JsonSerializerOptions.Default
            );
        }

        SecurityEvent securityEvent = new()
        {
            Id = Guid.NewGuid(),
            At = request.At ?? DateTime.UtcNow,
            Source = "fail2ban",
            EventType = normalisedEventType,
            Severity = severity,
            Host = request.Host,
            Jail = request.Jail,
            RemoteIp = request.Ip,
            DetailsJson = detailsJson,
        };

        await _securityLog.LogAsync(securityEvent, ct);

        // Broadcast a ban notification so the PWA/push agent's pipeline can fan it out to
        // every subscribed admin. Found/unban events stay quiet — the SignalR `security.event`
        // frame is enough for in-app awareness without spamming push.
        if (normalisedEventType == "fail2ban.ban")
        {
            try
            {
                await _notifications.BroadcastAsync(
                    NotificationKind.SystemMessage,
                    severity,
                    title: "IP banned by fail2ban",
                    body: $"{request.Ip} banned via jail {request.Jail} on {request.Host} at "
                        + $"{securityEvent.At:u}.",
                    relatedType: "IpReputation",
                    relatedId: request.Ip,
                    ct: ct
                );
            }
            catch (Exception ex)
            {
                // Notification failure must not fail the ingest — fail2ban will retry on its
                // own action.d cadence, and we've already written the SecurityEvent row.
                _logger.LogWarning(
                    ex,
                    "Failed to publish ban notification for {Ip} via {Jail}",
                    request.Ip,
                    request.Jail
                );
            }
        }

        return Ok(new { eventId = securityEvent.Id });
    }

    private static bool ConstantTimeEquals(string presented, string configured)
    {
        ReadOnlySpan<byte> a = Encoding.UTF8.GetBytes(presented);
        ReadOnlySpan<byte> b = Encoding.UTF8.GetBytes(configured);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
