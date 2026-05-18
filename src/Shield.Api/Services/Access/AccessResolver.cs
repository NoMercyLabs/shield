using System.Security.Claims;

namespace Shield.Api.Services.Access;

public sealed class AccessResolver : IAccessResolver
{
    private const string CacheKey = "Shield.AccessResolver.Cache";

    private readonly ShieldDbContext _db;
    private readonly IHttpContextAccessor _httpContext;
    private readonly IGithubAccessResolver _githubAccess;
    private readonly ILogger<AccessResolver> _logger;

    public AccessResolver(
        ShieldDbContext db,
        IHttpContextAccessor httpContext,
        IGithubAccessResolver githubAccess,
        ILogger<AccessResolver> logger
    )
    {
        _db = db;
        _httpContext = httpContext;
        _githubAccess = githubAccess;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> GetVisibleSourceIdsAsync(
        ClaimsPrincipal user,
        CancellationToken ct
    )
    {
        AccessCache cache = await ResolveAsync(user, ct);
        return cache.VisibleSourceIds;
    }

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, int sourceId, CancellationToken ct)
    {
        AccessCache cache = await ResolveAsync(user, ct);
        if (cache.IsAdmin)
            return true;
        return cache.Grants.TryGetValue(sourceId, out SourceAccessLevel _);
    }

    public async Task<bool> CanTriageAsync(ClaimsPrincipal user, int sourceId, CancellationToken ct)
    {
        AccessCache cache = await ResolveAsync(user, ct);
        if (cache.IsAdmin)
            return true;
        return cache.Grants.TryGetValue(sourceId, out SourceAccessLevel level)
            && level >= SourceAccessLevel.Triage;
    }

    private async Task<AccessCache> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        HttpContext? httpContext = _httpContext.HttpContext;
        if (httpContext is not null && httpContext.Items[CacheKey] is AccessCache cached)
            return cached;

        AccessCache resolved = await ComputeAsync(user, ct);
        if (httpContext is not null)
            httpContext.Items[CacheKey] = resolved;
        return resolved;
    }

    private async Task<AccessCache> ComputeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        bool isAdmin = user.IsInRole(ShieldRoles.Admin);
        if (isAdmin)
        {
            List<int> allSourceIds = await _db
                .Sources.AsNoTracking()
                .Select(source => source.Id)
                .ToListAsync(ct);
            return new(
                IsAdmin: true,
                VisibleSourceIds: allSourceIds,
                Grants: new Dictionary<int, SourceAccessLevel>()
            );
        }

        Guid? userId = TryGetUserId(user);
        if (userId is null)
        {
            return new(
                IsAdmin: false,
                VisibleSourceIds: [],
                Grants: new Dictionary<int, SourceAccessLevel>()
            );
        }

        List<int> groupIds = await _db
            .GroupMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId.Value)
            .Select(membership => membership.GroupId)
            .ToListAsync(ct);

        List<SourceAccess> grants = await _db
            .SourceAccesses.AsNoTracking()
            .Where(access =>
                access.UserId == userId.Value
                || (access.GroupId != null && groupIds.Contains(access.GroupId.Value))
            )
            .ToListAsync(ct);

        Dictionary<int, SourceAccessLevel> effective = new();
        foreach (SourceAccess grant in grants)
        {
            if (
                !effective.TryGetValue(grant.SourceId, out SourceAccessLevel current)
                || grant.Level > current
            )
                effective[grant.SourceId] = grant.Level;
        }

        // Layer GitHub-derived access on top of manual grants — manual rows can elevate
        // but the GitHub layer only raises a missing/lower-level grant up to the GitHub
        // role. Manual + GitHub means whichever is higher wins.
        try
        {
            GithubAccessSnapshot? gh = await _githubAccess.GetAccessAsync(userId.Value, ct);
            if (gh is not null)
            {
                foreach ((int sourceId, GithubSourceAccess access) in gh.SourceAccess)
                {
                    if (
                        !effective.TryGetValue(sourceId, out SourceAccessLevel current)
                        || access.Level > current
                    )
                        effective[sourceId] = access.Level;
                }
            }
        }
        catch (Exception ex)
        {
            // GitHub layer is additive — a failure here MUST NOT drop manual grants.
            _logger.LogWarning(
                ex,
                "GitHub access layer failed for user {UserId}; falling back to manual grants only",
                userId
            );
        }

        return new(IsAdmin: false, VisibleSourceIds: effective.Keys.ToList(), Grants: effective);
    }

    private static Guid? TryGetUserId(ClaimsPrincipal user)
    {
        string? raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Guid.TryParse(raw, out Guid parsed) ? parsed : null;
    }

    private sealed record AccessCache(
        bool IsAdmin,
        IReadOnlyList<int> VisibleSourceIds,
        IReadOnlyDictionary<int, SourceAccessLevel> Grants
    );
}
