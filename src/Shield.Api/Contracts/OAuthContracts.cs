using Shield.Core.Domain;

namespace Shield.Api.Contracts;

// Returned by /api/oauth/{provider}/start — UI redirects the browser here.
public sealed record OAuthStartResponse(string AuthorizationUrl, string State);

// Status flips connected=true once the callback persists a token row. DeviceFlowAvailable
// is GitHub-specific today — set when the runtime can resolve a baked-in or configured
// client_id and Shield:OAuth:GitHub:DeviceFlow:Enabled isn't disabled. Other providers
// always return false for that field.
public sealed record OAuthStatusResponse(
    OAuthProvider Provider,
    bool Connected,
    string? AccountLogin,
    string? AccountId,
    string? Scopes,
    DateTime? ExpiresAt,
    DateTime? UpdatedAt,
    bool DeviceFlowAvailable = false
);

// Device-flow contracts. flowId is the server-issued handle the SPA uses to poll; the
// real github device_code never crosses the wire to the browser.
public sealed record GitHubDeviceStartResponse(
    string FlowId,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval,
    // Pre-filled github.com/login/device URL when GitHub returns one — the SPA opens this
    // so the user_code field is already populated and the user just clicks Authorize.
    string? VerificationUriComplete = null
);

public sealed record GitHubDevicePollRequest(string FlowId);

public sealed record GitHubDevicePollResponse(string Status, GitHubDevicePollUser? User = null);

public sealed record GitHubDevicePollUser(string Login, string Id, string? AvatarUrl);

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
