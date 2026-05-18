namespace Shield.Api.Contracts;

public sealed record ApiTokenSummary(
    Guid Id,
    Guid UserId,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<int> SourceIdFilter,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    string? LastUsedIp,
    DateTime? RevokedAt
);

public sealed record CreateApiTokenRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    int? ExpiresInDays,
    IReadOnlyList<int>? SourceIdFilter
);

// The plaintext `Token` field is populated exactly once — at creation. There is no read-back.
public sealed record CreateApiTokenResponse(ApiTokenSummary Token, string Plaintext);
