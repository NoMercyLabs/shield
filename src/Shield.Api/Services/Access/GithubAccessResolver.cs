using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Shield.Core.Http;

namespace Shield.Api.Services.Access;

public sealed class GithubAccessResolver : IGithubAccessResolver
{
    private const string GithubProviderKey = "Github";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOAuthTokenStore _tokenStore;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<GithubAccessResolver> _logger;
    private readonly TimeSpan _cacheTtl;

    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();

    public GithubAccessResolver(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOAuthTokenStore tokenStore,
        IMemoryCache memoryCache,
        IConfiguration configuration,
        ILogger<GithubAccessResolver> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _tokenStore = tokenStore;
        _memoryCache = memoryCache;
        _logger = logger;
        int minutes = configuration.GetValue("Shield:Access:GithubCacheMinutes", 15);
        if (minutes <= 0)
            minutes = 15;
        _cacheTtl = TimeSpan.FromMinutes(minutes);
    }

    public async Task<GithubAccessSnapshot?> GetAccessAsync(Guid userId, CancellationToken ct)
    {
        if (
            _cache.TryGetValue(userId, out CacheEntry? cached)
            && cached.FetchedAt + _cacheTtl > DateTimeOffset.UtcNow
        )
            return cached.Snapshot;

        return await RefreshAsync(userId, ct);
    }

    public async Task<GithubAccessSnapshot?> RefreshAsync(Guid userId, CancellationToken ct)
    {
        GithubAccessSnapshot? snapshot = await ComputeAsync(userId, ct);
        if (snapshot is not null)
            _cache[userId] = new(snapshot, DateTimeOffset.UtcNow);
        else
            _cache.TryRemove(userId, out _);
        return snapshot;
    }

    public void Invalidate(Guid userId) => _cache.TryRemove(userId, out _);

    private async Task<GithubAccessSnapshot?> ComputeAsync(Guid userId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        ShieldUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);
        UserLoginInfo? githubLogin = logins.FirstOrDefault(login =>
            string.Equals(
                login.LoginProvider,
                GithubProviderKey,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (githubLogin is null || string.IsNullOrEmpty(githubLogin.ProviderKey))
            return null;

        OAuthTokenSnapshot? token = await _tokenStore.GetSigninAsync(
            OAuthProvider.Github,
            githubLogin.ProviderKey,
            ct
        );

        HttpClient http = _httpClientFactory.CreateClient("github");
        List<string> orgs = [];
        bool ownTokenWorked = false;
        if (token is not null && !string.IsNullOrEmpty(token.AccessToken))
        {
            try
            {
                orgs = await FetchOrgsAsync(http, token.AccessToken, ct);
                ownTokenWorked = true;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "GitHub /user/orgs failed for user {UserId} — scope may be missing read:org; trying admin-attested fallback",
                    userId
                );
            }
        }

        List<Source> sources = await db
            .Sources.AsNoTracking()
            .Where(source => source.Type == SourceType.GithubRepo)
            .ToListAsync(ct);

        Dictionary<int, GithubSourceAccess> result = new();
        if (ownTokenWorked && orgs.Count > 0)
        {
            HashSet<string> ownerSet = new(orgs, StringComparer.OrdinalIgnoreCase);
            foreach (Source source in sources)
            {
                (string? owner, string? _) = TryExtractOwnerRepo(source.ConfigJson);
                if (string.IsNullOrEmpty(owner))
                    continue;
                if (!ownerSet.Contains(owner))
                    continue;

                // TODO(per-repo-probe): tighten to actual collaborator role via
                //   GET /repos/{owner}/{repo}/collaborators/{login}/permission
                // and map admin|maintain|write → Triage, triage|read → Read. Org membership
                // alone grants Triage today, matching "Maintainer of many projects" semantics.
                result[source.Id] = new(SourceAccessLevel.Triage, $"org:{owner}");
            }
        }

        // Direct path produced grants — done.
        if (result.Count > 0)
            return new(result, DateTimeOffset.UtcNow, orgs);

        // Trust-by-admin-attestation fallback. Fires when the invitee's own token is
        // missing, can't enumerate orgs (pre-widening scope: read:user user:email,
        // before SIGNIN scope was widened to include read:org), or returns empty —
        // including pre-widening tokens whose /user/orgs returns [] because read:org
        // isn't granted. We ask the admin's connect-flow token "do you see this user
        // in any org you administer?" and treat 204 from /orgs/{org}/members/{login}
        // as a verified membership. Safe because the admin token IS the authority
        // for the orgs they administer.
        string? inviteeLogin = !string.IsNullOrEmpty(token?.AccountLogin)
            ? token!.AccountLogin
            : githubLogin.ProviderDisplayName;
        GithubAccessSnapshot? fallback = await TryAdminAttestationFallbackAsync(
            userManager,
            sources,
            inviteeLogin,
            http,
            ct
        );
        if (fallback is not null)
            return fallback;

        // No grants from either path — return an empty snapshot so callers can render
        // "zero sources from GitHub" rather than retry the resolve.
        return new(new Dictionary<int, GithubSourceAccess>(), DateTimeOffset.UtcNow, orgs);
    }

    private async Task<GithubAccessSnapshot?> TryAdminAttestationFallbackAsync(
        UserManager<ShieldUser> userManager,
        IReadOnlyList<Source> sources,
        string? inviteeLogin,
        HttpClient http,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(inviteeLogin))
            return null;

        OAuthTokenSnapshot? adminToken = await _tokenStore.GetAsync(OAuthProvider.Github, ct);
        if (adminToken is null || string.IsNullOrEmpty(adminToken.AccessToken))
            return null;

        // Identify the admin user who connected the integration so audit details can
        // record viaAdmin. The connect-flow row's LinkedUserId is the source of truth,
        // but it isn't on OAuthTokenSnapshot — re-read the IntegrationToken row.
        Guid? adminUserId = await TryResolveAdminUserIdAsync(userManager, adminToken, ct);

        string adminOrgsCacheKey = $"gh-access-fallback::admin-orgs::{adminToken.AccountLogin}";
        List<string> adminOrgs =
            await _memoryCache.GetOrCreateAsync(
                adminOrgsCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                    try
                    {
                        return await FetchOrgsAsync(http, adminToken.AccessToken, ct);
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Admin /user/orgs failed during attestation fallback — admin token may have lost read:org"
                        );
                        return [];
                    }
                }
            ) ?? [];

        if (adminOrgs.Count == 0)
            return null;

        // Only probe orgs that actually have at least one Shield source. No point
        // burning a membership probe on an org we wouldn't grant access to anyway.
        HashSet<string> adminOrgSet = new(adminOrgs, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<int>> ownerToSourceIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (Source source in sources)
        {
            (string? owner, string? _) = TryExtractOwnerRepo(source.ConfigJson);
            if (string.IsNullOrEmpty(owner))
                continue;
            if (!adminOrgSet.Contains(owner))
                continue;
            if (!ownerToSourceIds.TryGetValue(owner, out List<int>? bucket))
            {
                bucket = [];
                ownerToSourceIds[owner] = bucket;
            }
            bucket.Add(source.Id);
        }

        if (ownerToSourceIds.Count == 0)
            return null;

        Dictionary<int, GithubSourceAccess> result = new();
        List<string> matchedOrgs = [];
        foreach ((string owner, List<int> sourceIds) in ownerToSourceIds)
        {
            bool member = await IsOrgMemberCachedAsync(
                http,
                adminToken.AccessToken,
                owner,
                inviteeLogin,
                ct
            );
            if (!member)
                continue;

            matchedOrgs.Add(owner);
            foreach (int sourceId in sourceIds)
            {
                result[sourceId] = new(SourceAccessLevel.Triage, $"org-via-admin:{owner}");
            }
        }

        if (result.Count == 0 && matchedOrgs.Count == 0)
            return null;

        return new(
            result,
            DateTimeOffset.UtcNow,
            matchedOrgs,
            new(
                ViaAdminUserId: adminUserId ?? Guid.Empty,
                OrgsChecked: ownerToSourceIds.Count,
                OrgsMatched: matchedOrgs.Count
            )
        );
    }

    private async Task<Guid?> TryResolveAdminUserIdAsync(
        UserManager<ShieldUser> userManager,
        OAuthTokenSnapshot adminToken,
        CancellationToken ct
    )
    {
        // OAuthTokenSnapshot doesn't carry LinkedUserId, but the connect-flow row's
        // AccountLogin matches the admin's GitHub UserLoginInfo.ProviderDisplayName.
        // Cheap match keeps us from layering yet another store API just for audit text.
        if (string.IsNullOrEmpty(adminToken.AccountLogin))
            return null;
        List<ShieldUser> candidates = await userManager.Users.Where(user => true).ToListAsync(ct);
        foreach (ShieldUser candidate in candidates)
        {
            IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(candidate);
            UserLoginInfo? gh = logins.FirstOrDefault(login =>
                string.Equals(
                    login.LoginProvider,
                    GithubProviderKey,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (
                gh is not null
                && string.Equals(
                    gh.ProviderDisplayName,
                    adminToken.AccountLogin,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return candidate.Id;
        }
        return null;
    }

    private async Task<bool> IsOrgMemberCachedAsync(
        HttpClient http,
        string adminAccessToken,
        string org,
        string login,
        CancellationToken ct
    )
    {
        string cacheKey =
            $"gh-access-fallback::is-member::{org.ToLowerInvariant()}::{login.ToLowerInvariant()}";
        if (_memoryCache.TryGetValue(cacheKey, out bool cached))
            return cached;

        // GET /orgs/{org}/members/{username}
        //   204 — invitee is a member (public OR private, from admin's vantage)
        //   302 — admin isn't a member of {org}, treat as not-a-match
        //   404 — invitee is not a member
        // Anything else: treat as not-a-match and log; don't cache the failure so a
        // transient blip doesn't poison the next 15min window.
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            $"orgs/{Uri.EscapeDataString(org)}/members/{Uri.EscapeDataString(login)}"
        );
        AddAuth(request, adminAccessToken);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            bool isMember = response.StatusCode == HttpStatusCode.NoContent;
            _memoryCache.Set(cacheKey, isMember, _cacheTtl);
            return isMember;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(
                ex,
                "GitHub org membership probe failed for {Login} in {Org}",
                login,
                org
            );
            return false;
        }
    }

    private static async Task<List<string>> FetchOrgsAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        List<string> orgs = [];
        string? nextUrl = "user/orgs?per_page=100";
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using HttpRequestMessage request = new(HttpMethod.Get, nextUrl);
            AddAuth(request, accessToken);
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token revoked or scope insufficient — surface as no orgs rather than
                // killing the whole resolve. Caller falls through to manual grants.
                throw new HttpRequestException("github_token_invalid_or_missing_read_org");
            }
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct),
                cancellationToken: ct
            );
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                break;

            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                string? login = element.TryGetProperty("login", out JsonElement loginEl)
                    ? loginEl.GetString()
                    : null;
                if (!string.IsNullOrEmpty(login))
                    orgs.Add(login);
            }

            nextUrl = TryExtractNextLink(response);
        }
        return orgs;
    }

    private static (string? owner, string? repo) TryExtractOwnerRepo(string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return (null, null);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);
            string? owner = doc.RootElement.TryGetProperty("owner", out JsonElement ownerEl)
                ? ownerEl.GetString()
                : null;
            string? repo = doc.RootElement.TryGetProperty("repo", out JsonElement repoEl)
                ? repoEl.GetString()
                : null;
            return (owner, repo);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static void AddAuth(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
    }

    private static string? TryExtractNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
            return null;
        foreach (string headerValue in values)
        {
            foreach (string part in headerValue.Split(','))
            {
                string segment = part.Trim();
                if (segment.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                int lt = segment.IndexOf('<');
                int gt = segment.IndexOf('>');
                if (lt < 0 || gt <= lt)
                    continue;
                return segment.Substring(lt + 1, gt - lt - 1);
            }
        }
        return null;
    }

    private sealed record CacheEntry(GithubAccessSnapshot Snapshot, DateTimeOffset FetchedAt);
}
