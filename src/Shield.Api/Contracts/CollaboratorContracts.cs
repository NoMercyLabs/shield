namespace Shield.Api.Contracts;

// Lightweight shapes for the "pick a GitHub user/org to invite" picker in AccessView.
// Mirrors the bits of GitHub's REST responses the SPA actually renders — we deliberately
// don't pass the full upstream payload through (avoids leaking PII or rate-limit headers).

public sealed record GithubOrgSummary(
    string Login,
    string? Name,
    string? AvatarUrl,
    int? MemberCount
);

public sealed record GithubUserSummary(
    string Login,
    string? Name,
    string? Email,
    string? AvatarUrl,
    string GithubId
);

public sealed record GithubOrgListResponse(IReadOnlyList<GithubOrgSummary> Orgs);

public sealed record GithubMemberListResponse(
    IReadOnlyList<GithubUserSummary> Members,
    int Page,
    int PerPage,
    bool HasMore
);

public sealed record GithubUserSearchResponse(IReadOnlyList<GithubUserSummary> Users);
