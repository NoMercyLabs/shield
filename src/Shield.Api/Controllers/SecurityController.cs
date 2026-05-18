using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/security")]
[Authorize]
[NoApiToken]
public sealed class SecurityController : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int IpDetailEventLimit = 100;

    private readonly ShieldDbContext _db;
    private readonly ISecurityEventLogger _securityLog;

    public SecurityController(ShieldDbContext db, ISecurityEventLogger securityLog)
    {
        _db = db;
        _securityLog = securityLog;
    }

    [HttpGet("events")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<SecurityEventsPage>> Events(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] Severity? minSeverity = null,
        [FromQuery] string? source = null,
        [FromQuery] string? jail = null,
        [FromQuery] string? ip = null,
        [FromQuery] string? userName = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] DateTime? until = null,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        IQueryable<SecurityEvent> query = _db.SecurityEvents.AsQueryable();
        if (minSeverity.HasValue)
            query = query.Where(securityEvent => securityEvent.Severity >= minSeverity.Value);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(securityEvent => securityEvent.Source == source);
        if (!string.IsNullOrWhiteSpace(jail))
            query = query.Where(securityEvent => securityEvent.Jail == jail);
        if (!string.IsNullOrWhiteSpace(ip))
            query = query.Where(securityEvent => securityEvent.RemoteIp == ip);
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(securityEvent => securityEvent.UserName == userName);
        if (since.HasValue)
            query = query.Where(securityEvent => securityEvent.At >= since.Value);
        if (until.HasValue)
            query = query.Where(securityEvent => securityEvent.At <= until.Value);

        int total = await query.CountAsync(ct);
        List<SecurityEvent> rows = await query
            .OrderByDescending(securityEvent => securityEvent.At)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        IReadOnlyList<SecurityEventResponse> items = rows.Select(SecurityEventResponse.From)
            .ToList();
        return Ok(new SecurityEventsPage(items, total, page, pageSize));
    }

    [HttpGet("ips")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<IpReputationsPage>> Ips(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] bool bannedOnly = false,
        [FromQuery] string? search = null,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        IQueryable<IpReputation> query = _db.IpReputations.AsQueryable();
        if (bannedOnly)
            query = query.Where(reputation => reputation.CurrentlyBanned);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(reputation => reputation.Ip.Contains(search));

        int total = await query.CountAsync(ct);
        List<IpReputation> rows = await query
            .OrderByDescending(reputation => reputation.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        IReadOnlyList<IpReputationResponse> items = rows.Select(IpReputationResponse.From).ToList();
        return Ok(new IpReputationsPage(items, total, page, pageSize));
    }

    [HttpGet("ips/{ip}")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<IpDetailResponse>> IpDetail(string ip, CancellationToken ct)
    {
        IpReputation? reputation = await _db.IpReputations.FirstOrDefaultAsync(
            row => row.Ip == ip,
            ct
        );
        if (reputation is null)
            return NotFound();

        List<SecurityEvent> events = await _db
            .SecurityEvents.Where(securityEvent => securityEvent.RemoteIp == ip)
            .OrderByDescending(securityEvent => securityEvent.At)
            .Take(IpDetailEventLimit)
            .ToListAsync(ct);

        return Ok(
            new IpDetailResponse(
                IpReputationResponse.From(reputation),
                events.Select(SecurityEventResponse.From).ToList()
            )
        );
    }

    [HttpPost("ips/{ip}/notes")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<IpReputationResponse>> UpdateNotes(
        string ip,
        [FromBody] UpdateNotesRequest request,
        CancellationToken ct
    )
    {
        IpReputation? reputation = await _db.IpReputations.FirstOrDefaultAsync(
            row => row.Ip == ip,
            ct
        );
        if (reputation is null)
            return NotFound();
        reputation.Notes = request.Notes;
        await _db.SaveChangesAsync(ct);
        return Ok(IpReputationResponse.From(reputation));
    }

    // Records the admin's intent to ban; the actual outbound fail2ban-client invocation is
    // Wave-F (requires SSH or a remote API to the fail2ban host, which is out of scope here).
    // For now: write a SecurityEvent + broadcast it so the UI can render "Awaiting fail2ban
    // confirmation". When fail2ban subsequently bans the IP, its ingest event flips the
    // CurrentlyBanned flag, closing the loop.
    [HttpPost("ips/{ip}/request-ban")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<IActionResult> RequestBan(
        string ip,
        [FromBody] RequestBanRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Jail) || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "jail and reason are required." });

        string detailsJson = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                reason = request.Reason,
                hours = request.Hours,
                requestedBy = User.Identity?.Name,
            }
        );

        // TODO(wave-f): outbound fail2ban-client invocation. Today: emit the intent + leave
        // the UI showing "Awaiting fail2ban confirmation" until a matching fail2ban.ban
        // event arrives via /api/security/fail2ban/event.
        await _securityLog.LogAsync(
            source: "shield",
            eventType: "ban.requested",
            severity: Severity.Medium,
            remoteIp: ip,
            userName: User.Identity?.Name,
            jail: request.Jail,
            detailsJson: detailsJson,
            ct: ct
        );

        return Accepted(new { ip, jail = request.Jail });
    }

    [HttpGet("hosts")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<HostsResponse>> Hosts(CancellationToken ct)
    {
        // Group-project into an anonymous record server-side so SQLite can translate the
        // aggregate, then convert to the public HostSummary record on the client.
        var raw = await _db
            .SecurityEvents.Where(securityEvent => securityEvent.Host != null)
            .GroupBy(securityEvent => securityEvent.Host!)
            .Select(group => new
            {
                Host = group.Key,
                LastSeenAt = group.Max(securityEvent => securityEvent.At),
                EventCount = group.Count(),
            })
            .ToListAsync(ct);

        List<HostSummary> hosts = raw.Select(row => new HostSummary(
                row.Host,
                row.LastSeenAt,
                row.EventCount
            ))
            .OrderByDescending(host => host.LastSeenAt)
            .ToList();
        return Ok(new HostsResponse(hosts));
    }
}
