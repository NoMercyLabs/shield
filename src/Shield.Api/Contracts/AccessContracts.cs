namespace Shield.Api.Contracts;

public sealed record SourceGroupResponse(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    IReadOnlyList<GroupMemberDto> Members
);

public sealed record GroupMemberDto(Guid UserId, string Username, DateTime AddedAt);

public sealed record CreateGroupRequest(string Name, string? Description);

public sealed record UpdateGroupRequest(string Name, string? Description);

public sealed record AddGroupMemberRequest(string? Username, string? Email);

public sealed record AddGroupMemberResponse(GroupMemberDto Member);

public sealed record SourceGrantResponse(
    int Id,
    int SourceId,
    Guid? UserId,
    string? Username,
    int? GroupId,
    string? GroupName,
    SourceAccessLevel Level,
    DateTime GrantedAt,
    Guid? GrantedBy
);

public sealed record GrantSourceRequest(
    Guid? UserId,
    int? GroupId,
    SourceAccessLevel Level = SourceAccessLevel.Read
);

public sealed record SourceGrantsResponse(int SourceId, IReadOnlyList<SourceGrantResponse> Grants);

public sealed record AccessUserDto(
    Guid Id,
    string Username,
    string? Email,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt
);

// Owner-issued invite link. The user is NOT created up-front — see Invite domain entity
// for why. SourceGroupIds is the list of groups the invitee will be added to on accept.
//
// Exactly ONE of Email / ExternalIdentity must be set:
// - Email: legacy path — invitee clicks the link, signs in with any provider, account is
//   created if the verified provider email matches.
// - ExternalIdentity: GitHub-picker path — invite is pre-bound to a specific provider account
//   (subject id), so only that exact GitHub user can claim it. The provider's public email,
//   if any, populates Email for display + the optional courtesy notification email.
public sealed record InviteUserRequest(
    string? Email,
    string Role,
    IReadOnlyList<int>? SourceGroupIds,
    InviteExternalIdentity? ExternalIdentity = null
);

// Pre-bound external identity. Provider is currently always "github" (case-insensitive).
// SubjectId is the provider's numeric stable id (NOT login — logins can be renamed).
public sealed record InviteExternalIdentity(
    string Provider,
    string SubjectId,
    string Login,
    string? DisplayName,
    string? AvatarUrl,
    string? Email
);

public sealed record InviteUserResponse(
    Guid InviteId,
    string Email,
    string Role,
    IReadOnlyList<int> SourceGroupIds,
    DateTime ExpiresAt,
    string AcceptUrl,
    bool EmailSent,
    string? EmailSkipReason,
    InvitePreBoundIdentity? PreBound = null
);

public sealed record InvitePreBoundIdentity(string Provider, string SubjectId, string Login);

// Public surface — returned by GET /api/access/invite/{token} so the accept page can render
// context before the user signs in. Deliberately omits the email + raw token + creator id.
public sealed record PublicInvitePreview(
    string Role,
    IReadOnlyList<string> SourceGroupNames,
    string InviterLogin,
    DateTime ExpiresAt
);

public sealed record PendingInviteResponse(
    Guid Id,
    string Email,
    string Role,
    IReadOnlyList<int> SourceGroupIds,
    IReadOnlyList<string> SourceGroupNames,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? LastSentAt,
    int ResendCount,
    string? InviterLogin,
    InvitePreBoundIdentity? PreBound = null,
    // Admin-only endpoint already; surface the token so the Pending Invitations table can
    // build the accept URL for Copy/Share buttons without a per-row round-trip.
    string? Token = null
);

// AcceptanceTicket is null when the caller has ALREADY authenticated via the regular OAuth
// auth-code popup flow and lands back on /accept-invite with a session cookie. The
// controller falls back to the authenticated user's bound external login to verify identity.
public sealed record AcceptInviteRequest(string Token, string? AcceptanceTicket = null);

public sealed record AcceptInviteResponse(
    Guid UserId,
    string Username,
    string Role,
    IReadOnlyList<int> SourceGroupIds
);

public sealed record RefreshGithubAccessResponse(
    Guid UserId,
    int SourceCount,
    IReadOnlyList<string> Orgs,
    bool HasGithubLogin
);
