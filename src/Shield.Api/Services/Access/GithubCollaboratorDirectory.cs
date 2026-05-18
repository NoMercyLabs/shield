using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Shield.Core.Http;

namespace Shield.Api.Services.Access;

// Implementation notes
// --------------------
// * Uses the named "github" HttpClient — it's wired with GitHubRateLimitHandler, so all the
//   primary/secondary rate-limit accounting + backoff happens upstream of us.
// * Reads the admin's connected token via IOAuthTokenStore (the row keyed by empty Subject —
//   the connect-flow row, NOT the per-signin row).
// * Caches: orgs 5min per token, members 10min per (token, org, page). Search is uncached.
// * 401 → GithubTokenInvalidException (controller maps to 409 { action: reconnect }).
//   Anything else surfaces as HttpRequestException for the controller to translate to 502.
public sealed class GithubCollaboratorDirectory : IGithubCollaboratorDirectory
{
    private static readonly TimeSpan OrgsCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MembersCacheTtl = TimeSpan.FromMinutes(10);
    private const int MaxPerPage = 100;
    private const int MaxSearchResults = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOAuthTokenStore _tokenStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GithubCollaboratorDirectory> _logger;

    public GithubCollaboratorDirectory(
        IHttpClientFactory httpClientFactory,
        IOAuthTokenStore tokenStore,
        IMemoryCache cache,
        ILogger<GithubCollaboratorDirectory> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GithubOrgSummary>?> ListOrgsAsync(CancellationToken ct)
    {
        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(OAuthProvider.Github, ct);
        if (token is null)
            return null;

        string cacheKey = $"gh-orgs::{TokenFingerprint(token.AccessToken)}";
        if (
            _cache.TryGetValue(cacheKey, out IReadOnlyList<GithubOrgSummary>? cached)
            && cached is not null
        )
            return cached;

        HttpClient http = _httpClientFactory.CreateClient("github");
        using HttpRequestMessage request = new(HttpMethod.Get, "user/orgs?per_page=100");
        AddAuth(request, token.AccessToken);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GithubTokenInvalidException();
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        List<GithubOrgSummary> orgs = [];
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string login = element.TryGetProperty("login", out JsonElement loginEl)
                ? loginEl.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(login))
                continue;
            string? avatar = element.TryGetProperty("avatar_url", out JsonElement avatarEl)
                ? avatarEl.GetString()
                : null;
            string? description = element.TryGetProperty("description", out JsonElement descEl)
                ? descEl.GetString()
                : null;
            // /user/orgs returns minimal shape — name + member_count don't surface here.
            // The SPA renders login when name is missing, so this is fine for the picker.
            orgs.Add(new(login, description, avatar, null));
        }

        _cache.Set(cacheKey, (IReadOnlyList<GithubOrgSummary>)orgs, OrgsCacheTtl);
        return orgs;
    }

    public async Task<GithubMemberListResponse?> ListMembersAsync(
        string org,
        int page,
        int perPage,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(org))
            return new([], page, perPage, false);

        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(OAuthProvider.Github, ct);
        if (token is null)
            return null;

        if (page < 1)
            page = 1;
        if (perPage < 1 || perPage > MaxPerPage)
            perPage = MaxPerPage;

        string cacheKey =
            $"gh-org-members::{TokenFingerprint(token.AccessToken)}::{org.ToLowerInvariant()}::{page}::{perPage}";
        if (
            _cache.TryGetValue(cacheKey, out GithubMemberListResponse? cached) && cached is not null
        )
            return cached;

        HttpClient http = _httpClientFactory.CreateClient("github");
        string listUrl = $"orgs/{Uri.EscapeDataString(org)}/members?per_page={perPage}&page={page}";
        using HttpRequestMessage listRequest = new(HttpMethod.Get, listUrl);
        AddAuth(listRequest, token.AccessToken);

        using HttpResponseMessage listResponse = await http.SendAsync(listRequest, ct);
        if (listResponse.StatusCode == HttpStatusCode.Unauthorized)
            throw new GithubTokenInvalidException();
        listResponse.EnsureSuccessStatusCode();

        bool hasMore = HasNextLink(listResponse);
        using JsonDocument doc = await JsonDocument.ParseAsync(
            await listResponse.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            GithubMemberListResponse empty = new([], page, perPage, false);
            _cache.Set(cacheKey, empty, MembersCacheTtl);
            return empty;
        }

        // The /orgs/{org}/members listing returns minimal user shape (login + id + avatar).
        // The picker wants name + public email too — that means a /users/{login} fan-out per
        // row. Sequential keeps the rate-limit handler's bucket bookkeeping simple; per-page
        // ceiling is 100 so worst case is one page = ~100 cheap calls.
        List<GithubUserSummary> members = [];
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string login = element.TryGetProperty("login", out JsonElement loginEl)
                ? loginEl.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(login))
                continue;
            string githubId = element.TryGetProperty("id", out JsonElement idEl)
                ? idEl.GetRawText()
                : string.Empty;
            string? avatar = element.TryGetProperty("avatar_url", out JsonElement avatarEl)
                ? avatarEl.GetString()
                : null;

            GithubUserSummary enriched = await FetchUserDetailsAsync(
                http,
                login,
                token.AccessToken,
                githubId,
                avatar,
                ct
            );
            members.Add(enriched);
        }

        GithubMemberListResponse result = new(members, page, perPage, hasMore);
        _cache.Set(cacheKey, result, MembersCacheTtl);
        return result;
    }

    public async Task<IReadOnlyList<GithubUserSummary>?> SearchUsersAsync(
        string query,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(OAuthProvider.Github, ct);
        if (token is null)
            return null;

        HttpClient http = _httpClientFactory.CreateClient("github");
        string url = $"search/users?q={Uri.EscapeDataString(query)}&per_page={MaxSearchResults}";
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        AddAuth(request, token.AccessToken);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GithubTokenInvalidException();
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return [];
        if (
            !doc.RootElement.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array
        )
            return [];

        List<GithubUserSummary> users = [];
        foreach (JsonElement element in items.EnumerateArray())
        {
            string login = element.TryGetProperty("login", out JsonElement loginEl)
                ? loginEl.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrEmpty(login))
                continue;
            string githubId = element.TryGetProperty("id", out JsonElement idEl)
                ? idEl.GetRawText()
                : string.Empty;
            string? avatar = element.TryGetProperty("avatar_url", out JsonElement avatarEl)
                ? avatarEl.GetString()
                : null;

            // Search results are intentionally NOT enriched with /users/{login} — search is
            // already a 30/min budget; doubling the calls would burn it on the first probe.
            // The SPA can call /users/search and then ListMembers if the admin lands on a
            // matched org; ad-hoc users go in name-only.
            users.Add(new(login, null, null, avatar, githubId));
            if (users.Count >= MaxSearchResults)
                break;
        }
        return users;
    }

    private async Task<GithubUserSummary> FetchUserDetailsAsync(
        HttpClient http,
        string login,
        string accessToken,
        string githubId,
        string? fallbackAvatar,
        CancellationToken ct
    )
    {
        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                $"users/{Uri.EscapeDataString(login)}"
            );
            AddAuth(request, accessToken);
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new GithubTokenInvalidException();
            if (!response.IsSuccessStatusCode)
                return new(login, null, null, fallbackAvatar, githubId);

            using JsonDocument doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct),
                cancellationToken: ct
            );
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new(login, null, null, fallbackAvatar, githubId);

            string? name = doc.RootElement.TryGetProperty("name", out JsonElement nameEl)
                ? nameEl.GetString()
                : null;
            string? email = doc.RootElement.TryGetProperty("email", out JsonElement emailEl)
                ? emailEl.GetString()
                : null;
            string? avatar = doc.RootElement.TryGetProperty("avatar_url", out JsonElement avatarEl)
                ? avatarEl.GetString()
                : fallbackAvatar;
            string id = doc.RootElement.TryGetProperty("id", out JsonElement idEl)
                ? idEl.GetRawText()
                : githubId;
            return new(login, name, email, avatar, id);
        }
        catch (GithubTokenInvalidException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A single member's /users/{login} probe failing shouldn't drop the entire list.
            _logger.LogDebug(
                ex,
                "Failed to enrich GitHub user {Login}; falling back to listing fields",
                login
            );
            return new(login, null, null, fallbackAvatar, githubId);
        }
    }

    private static void AddAuth(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
    }

    // Stable but non-reversible cache key fragment — keeps the plaintext token out of the
    // MemoryCache key space (heap dumps / diagnostics).
    private static string TokenFingerprint(string token) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)),
            0,
            6
        );

    private static bool HasNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
            return false;
        foreach (string header in values)
        {
            // Cheap substring check — full RFC 5988 parse isn't needed for a yes/no answer.
            if (header.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
