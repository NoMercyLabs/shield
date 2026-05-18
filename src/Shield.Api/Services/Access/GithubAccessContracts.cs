namespace Shield.Api.Services.Access;

// GitHub-derived access layer. Mirrors the user's GitHub org/repo permissions into Shield's
// per-source access map so a teammate who already has access to the underlying repos on
// GitHub sees the matching Shield sources without an admin pre-populating manual grants.
//
// Caches per user for a config-driven TTL (default 15min) — refresh on signin + manual via
// AccessController. Uses the target USER'S own stored signin token; never the admin's.
public interface IGithubAccessResolver
{
    Task<GithubAccessSnapshot?> GetAccessAsync(Guid userId, CancellationToken ct);
    Task<GithubAccessSnapshot?> RefreshAsync(Guid userId, CancellationToken ct);
    void Invalidate(Guid userId);
}

public sealed record GithubAccessSnapshot(
    IReadOnlyDictionary<int, GithubSourceAccess> SourceAccess,
    DateTimeOffset FetchedAt,
    IReadOnlyList<string> OrgMemberships,
    // Set when the resolver fell back to admin-attestation because the user's own
    // token couldn't enumerate orgs (missing read:org scope on pre-widening tokens
    // or revoked/expired). Controllers surface this in audit details so we can
    // distinguish "user has org access" from "admin vouches for user".
    GithubFallbackDiagnostics? Fallback = null
);

public sealed record GithubSourceAccess(SourceAccessLevel Level, string Provenance);

public sealed record GithubFallbackDiagnostics(
    Guid ViaAdminUserId,
    int OrgsChecked,
    int OrgsMatched
);
