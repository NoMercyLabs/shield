using Shield.Core.Domain;

namespace Shield.Api.Contracts;

// Returned by /api/oauth/{provider}/start — UI redirects the browser here.
public sealed record OAuthStartResponse(string AuthorizationUrl, string State);

// Status flips connected=true once the callback persists a token row.
public sealed record OAuthStatusResponse(
    OAuthProvider Provider,
    bool Connected,
    string? AccountLogin,
    string? AccountId,
    string? Scopes,
    DateTime? ExpiresAt,
    DateTime? UpdatedAt
);

public sealed record SlackChannelInfo(string Id, string Name, bool IsPrivate);

public sealed record SlackChannelsResponse(IReadOnlyList<SlackChannelInfo> Channels);

// Surface for the "Pick from GitHub" repo picker. Mirrors the fields we care about
// from GitHub's /user/repos response — id is the numeric repo id (handy for client-side
// dedup keys), FullName is owner/name pre-joined for search filters.
public sealed record GitHubRepoEntry(
    long Id,
    string Owner,
    string Name,
    string FullName,
    string? Description,
    string? DefaultBranch,
    bool Private,
    bool Archived,
    bool Fork,
    string? Language
);

public sealed record GitHubRepoListResponse(IReadOnlyList<GitHubRepoEntry> Repos, int Total);
