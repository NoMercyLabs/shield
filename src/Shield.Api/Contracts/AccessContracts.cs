using Shield.Core.Domain;

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

public sealed record InviteUserRequest(
    string Username,
    string Password,
    string Role,
    string? Email,
    IReadOnlyList<InviteGrant>? Grants
);

public sealed record InviteGrant(int SourceId, SourceAccessLevel Level = SourceAccessLevel.Read);

public sealed record InviteUserResponse(
    Guid UserId,
    string Username,
    string Role,
    IReadOnlyList<SourceGrantResponse> Grants,
    string LoginUrl
);
