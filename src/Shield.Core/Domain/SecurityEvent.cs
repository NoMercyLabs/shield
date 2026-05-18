namespace Shield.Core.Domain;

// Append-only correlation log. Each row is a single observation from one of Shield's own
// detectors (login.failed, session.revoked_cookie_replay, rate.limit, crawler.detected)
// or from an external fail2ban deployment posting through /api/security/fail2ban/event.
//
// Shield NEVER enforces from this table — fail2ban is the enforcer. The IpReputation
// rollup mirrors fail2ban's view of the current ban state for display in the UI.
//
// EventType naming convention: `area.specific_signal` in snake_case. Each eventType
// gets a human-readable label + body in the SPA's i18n catalog under the
// `security.events.<eventType>` key family so the Security view can render a sentence
// instead of a developer slug.
public sealed class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime At { get; set; }

    // Free-form source identifier. Shield-internal emitters use the "shield.<area>"
    // convention ("shield.auth", "shield.ratelimit", "shield.crawler"); external
    // ingesters use the upstream tool name ("fail2ban", "linux.auth", "nginx", "caddy").
    public string Source { get; set; } = "";

    // Dot-namespaced event type. Examples: "login.failed",
    // "session.revoked_cookie_replay", "session.stale_cookie_presented", "rate.limit",
    // "crawler.detected", "fail2ban.ban", "fail2ban.unban", "fail2ban.found".
    public string EventType { get; set; } = "";

    public Severity Severity { get; set; }

    // Host that reported the event (fail2ban container name, docker host, etc.).
    // Null for events Shield emits about its own request pipeline.
    public string? Host { get; set; }

    // fail2ban jail name when Source="fail2ban"; null otherwise.
    public string? Jail { get; set; }

    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
    public string? UserName { get; set; }
    public string? Path { get; set; }
    public string? DetailsJson { get; set; }
}
