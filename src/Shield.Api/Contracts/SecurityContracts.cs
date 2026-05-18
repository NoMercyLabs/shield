namespace Shield.Api.Contracts;

public sealed record SecurityEventResponse(
    Guid Id,
    DateTime At,
    string Source,
    string EventType,
    Severity Severity,
    string? Host,
    string? Jail,
    string? RemoteIp,
    string? UserAgent,
    string? UserName,
    string? Path,
    string? DetailsJson
)
{
    public static SecurityEventResponse From(SecurityEvent securityEvent) =>
        new(
            securityEvent.Id,
            securityEvent.At,
            securityEvent.Source,
            securityEvent.EventType,
            securityEvent.Severity,
            securityEvent.Host,
            securityEvent.Jail,
            securityEvent.RemoteIp,
            securityEvent.UserAgent,
            securityEvent.UserName,
            securityEvent.Path,
            securityEvent.DetailsJson
        );
}

public sealed record SecurityEventsPage(
    IReadOnlyList<SecurityEventResponse> Items,
    int Total,
    int Page,
    int PageSize
);

public sealed record SecurityEventFilter(
    int? Page,
    int? PageSize,
    Severity? MinSeverity,
    string? Source,
    string? Jail,
    string? Ip,
    string? UserName,
    DateTime? Since,
    DateTime? Until
);

public sealed record IpReputationResponse(
    int Id,
    string Ip,
    int EventCount,
    int Score,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    string? LastJail,
    DateTime? LastBannedAt,
    DateTime? LastUnbannedAt,
    bool CurrentlyBanned,
    string? Notes,
    string? Country
)
{
    public static IpReputationResponse From(IpReputation reputation) =>
        new(
            reputation.Id,
            reputation.Ip,
            reputation.EventCount,
            reputation.Score,
            reputation.FirstSeenAt,
            reputation.LastSeenAt,
            reputation.LastJail,
            reputation.LastBannedAt,
            reputation.LastUnbannedAt,
            reputation.CurrentlyBanned,
            reputation.Notes,
            reputation.Country
        );
}

public sealed record IpReputationsPage(
    IReadOnlyList<IpReputationResponse> Items,
    int Total,
    int Page,
    int PageSize
);

public sealed record HostSummary(string Host, DateTime LastSeenAt, int EventCount);

public sealed record HostsResponse(IReadOnlyList<HostSummary> Items);

// Inbound payload from fail2ban's action.d/shield.conf. `matches` is an opaque array of
// log lines provided by the upstream jail's filter — stored verbatim in DetailsJson.
public sealed record Fail2BanIngestRequest(
    string Host,
    string Jail,
    string EventType,
    string Ip,
    DateTime? At,
    string[]? Matches
);

public sealed record RequestBanRequest(string Jail, string Reason, int? Hours);

public sealed record UpdateNotesRequest(string? Notes);

public sealed record IpDetailResponse(
    IpReputationResponse Reputation,
    IReadOnlyList<SecurityEventResponse> RecentEvents
);
