namespace Shield.Api.Services.Access;

// Read-side facade over the GitHub orgs/members/search endpoints. The connect-flow OAuth
// token (the admin's "Connect GitHub" row in IntegrationTokens) is the credential — Shield
// never asks the invitee for theirs.
//
// Each method returns null when no GitHub OAuth row is connected. Throws GithubTokenInvalid
// when the upstream call comes back 401 — the controller maps that to 409 so the SPA can
// surface a "reconnect GitHub" CTA without ambiguity vs other failure modes.
public interface IGithubCollaboratorDirectory
{
    Task<IReadOnlyList<GithubOrgSummary>?> ListOrgsAsync(CancellationToken ct);

    Task<GithubMemberListResponse?> ListMembersAsync(
        string org,
        int page,
        int perPage,
        CancellationToken ct
    );

    Task<IReadOnlyList<GithubUserSummary>?> SearchUsersAsync(string query, CancellationToken ct);
}

// Signals the saved admin token failed upstream auth (revoked, expired, or scopes pruned).
// Caller (CollaboratorsController) converts this to 409 { action: "reconnect" }.
public sealed class GithubTokenInvalidException : Exception
{
    public GithubTokenInvalidException()
        : base("GitHub OAuth token is invalid or revoked.") { }
}
