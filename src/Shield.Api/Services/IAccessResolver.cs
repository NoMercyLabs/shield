using System.Security.Claims;

namespace Shield.Api.Services;

// Per-request authorization gate for source-scoped operations. Admins short-circuit to
// "see everything, triage everything"; everyone else gets the union of direct
// SourceAccess.UserId == them and SourceAccess.GroupId IN their GroupMemberships.
//
// Scoped because the underlying ShieldDbContext is scoped. Results are memoised on
// HttpContext.Items so a single request resolving visibility for the list endpoint
// then re-checking CanRead on one row doesn't hit the DB twice.
public interface IAccessResolver
{
    Task<IReadOnlyList<int>> GetVisibleSourceIdsAsync(ClaimsPrincipal user, CancellationToken ct);
    Task<bool> CanReadAsync(ClaimsPrincipal user, int sourceId, CancellationToken ct);
    Task<bool> CanTriageAsync(ClaimsPrincipal user, int sourceId, CancellationToken ct);
}
