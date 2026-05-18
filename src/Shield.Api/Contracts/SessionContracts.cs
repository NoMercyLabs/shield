namespace Shield.Api.Contracts;

public sealed record SessionInfo(
    Guid Id,
    Guid UserId,
    string? Username,
    string? UserAgent,
    string? RemoteIp,
    DateTime CreatedAt,
    DateTime LastActiveAt,
    bool IsCurrent
);

public sealed record SessionListResponse(IReadOnlyList<SessionInfo> Sessions);

public sealed record RevokeOthersResponse(int Revoked);
