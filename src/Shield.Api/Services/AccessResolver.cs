using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services;

public sealed class AccessResolver : IAccessResolver
{
    private const string CacheKey = "Shield.AccessResolver.Cache";

    private readonly ShieldDbContext _db;
    private readonly IHttpContextAccessor _httpContext;

    public AccessResolver(ShieldDbContext db, IHttpContextAccessor httpContext)
    {
        _db = db;
        _httpContext = httpContext;
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
            return new AccessCache(
                IsAdmin: true,
                VisibleSourceIds: allSourceIds,
                Grants: new Dictionary<int, SourceAccessLevel>()
            );
        }

        Guid? userId = TryGetUserId(user);
        if (userId is null)
        {
            return new AccessCache(
                IsAdmin: false,
                VisibleSourceIds: Array.Empty<int>(),
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

        return new AccessCache(
            IsAdmin: false,
            VisibleSourceIds: effective.Keys.ToList(),
            Grants: effective
        );
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
