namespace Shield.Core.Domain;

// Per-IP rollup of correlated SecurityEvent activity. Observation-only — Shield does
// NOT use these rows to block traffic. `CurrentlyBanned` mirrors fail2ban's view as
// reported via /api/security/fail2ban/event. Score is a weighted informational signal
// the operator can sort by; weighting lives in ISecurityEventLogger.
public sealed class IpReputation
{
    public int Id { get; set; }

    // Canonical textual form. Unique. IPv6 stored compressed; mapped ::ffff:1.2.3.4
    // is normalised to its v4 form by the writer before upsert.
    public string Ip { get; set; } = "";

    // Rolling 30-day window count maintained by ISecurityEventLogger.
    public int EventCount { get; set; }

    // Weighted score: Low=1, Medium=3, High=8, Critical=20. Resets to zero when the
    // 30-day window slides past the last contributing event.
    public int Score { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    // Most recent fail2ban jail that banned this IP. Null when no ban has been ingested.
    public string? LastJail { get; set; }

    public DateTime? LastBannedAt { get; set; }
    public DateTime? LastUnbannedAt { get; set; }

    // Mirror of fail2ban's current view, NOT Shield's enforcement. Flips true on
    // fail2ban.ban ingestion, false on fail2ban.unban ingestion.
    public bool CurrentlyBanned { get; set; }

    // Operator-attached free-form note. Surfaces in the IP detail drawer.
    public string? Notes { get; set; }

    // Two-letter country code populated by an optional MaxMind GeoLite2 lookup.
    // Null when no GeoIP database is configured or the lookup fails.
    public string? Country { get; set; }
}
