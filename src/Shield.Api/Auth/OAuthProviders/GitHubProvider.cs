using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Http;

namespace Shield.Api.Auth.OAuthProviders;

public sealed class GitHubProvider : IOAuthProvider
{
    public const string AuthorizeUrl = "https://github.com/login/oauth/authorize";
    public const string TokenUrl = "https://github.com/login/oauth/access_token";
    public const string UserUrl = "https://api.github.com/user";
    public const string RevokeUrlTemplate = "https://api.github.com/applications/{0}/grant";

    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public OAuthProvider Provider => OAuthProvider.Github;
    public string DefaultScopes => "read:user public_repo";

    // GitHub signin needs read:user (login + id) and user:email so the callback can match by email.
    // read:org is required so GithubAccessResolver can mirror the user's org memberships onto
    // Shield's source ACL post-signin. Existing tokens minted under the narrower scope still
    // work for signin itself; the access mirror just returns an empty org list until the user
    // re-signs-in once and consents to the new scope.
    public string SigninDefaultScopes => "read:user user:email read:org";
    public bool SupportsPkce => true;

    public string BuildAuthorizationUrl(
        OAuthClientConfig config,
        string state,
        string codeChallenge,
        string scopes
    )
    {
        Dictionary<string, string> query = new()
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["state"] = state,
            ["scope"] = scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["allow_signup"] = "false",
        };
        return AuthorizeUrl + "?" + UrlForm.Encode(query);
    }

    public async Task<OAuthTokenSnapshot> ExchangeCodeAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["client_secret"] = config.ClientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = config.RedirectUri,
                    ["code_verifier"] = codeVerifier,
                }
            ),
        };
        request.Headers.Accept.Add(new("application/json"));

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        GitHubTokenResponse? body = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            throw new InvalidOperationException("GitHub returned no access token.");

        (string login, string id) = await ProbeUserAsync(http, body.AccessToken, ct);

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return new(
            OAuthProvider.Github,
            body.AccessToken,
            body.RefreshToken,
            expiresAt,
            body.Scope ?? string.Empty,
            login,
            id,
            null
        );
    }

    public async Task<OAuthTokenSnapshot?> RefreshAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot current,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
            return null;

        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["client_secret"] = config.ClientSecret,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                }
            ),
        };
        request.Headers.Accept.Add(new("application/json"));

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        GitHubTokenResponse? body = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            return null;

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return current with
        {
            AccessToken = body.AccessToken,
            RefreshToken = body.RefreshToken ?? current.RefreshToken,
            ExpiresAt = expiresAt,
        };
    }

    public async Task RevokeAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot token,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        string url = string.Format(
            CultureInfo.InvariantCulture,
            RevokeUrlTemplate,
            config.ClientId
        );
        using HttpRequestMessage request = new(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(new { access_token = token.AccessToken }),
        };
        string basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")
        );
        request.Headers.Authorization = new("Basic", basic);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            // Best-effort: ignore the response — local row is being deleted anyway.
        }
        catch
        {
            // Best-effort: local disconnect still proceeds.
        }
    }

    private static async Task<(string login, string id)> ProbeUserAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserUrl);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
        request.Headers.Accept.Add(new("application/vnd.github+json"));

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("(unknown)", "");

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        string login = root.TryGetProperty("login", out JsonElement loginElement)
            ? loginElement.GetString() ?? "(unknown)"
            : "(unknown)";
        string id = root.TryGetProperty("id", out JsonElement idElement)
            ? idElement.GetRawText()
            : string.Empty;
        return (login, id);
    }

    public string BuildSigninAuthorizationUrl(
        OAuthClientConfig config,
        string state,
        string codeChallenge,
        string scopes
    ) => BuildAuthorizationUrl(config, state, codeChallenge, scopes);

    public async Task<IReadOnlyList<RepositorySummary>?> ListRepositoriesAsync(
        string accessToken,
        RepositoryListOptions options,
        CancellationToken ct
    )
    {
        string affiliation = string.IsNullOrWhiteSpace(options.Affiliation)
            ? "owner,collaborator,organization_member"
            : options.Affiliation!;
        IReadOnlyList<GitHubRepoEntry> entries = await ListReposAsync(
            accessToken,
            affiliation,
            options.PerPage,
            options.MaxRepositories,
            ct
        );
        List<RepositorySummary> normalised = new(entries.Count);
        foreach (GitHubRepoEntry entry in entries)
        {
            normalised.Add(
                new(
                    entry.Owner,
                    entry.Name,
                    entry.FullName,
                    entry.Description,
                    entry.DefaultBranch,
                    entry.Private,
                    entry.Archived,
                    entry.Fork,
                    entry.Language
                )
            );
        }
        return normalised;
    }

    // Pages through /user/repos following the RFC 5988 Link header. Caps at MaxRepos to
    // protect against runaway accounts; perPage is the upstream page size (max 100 per GitHub docs).
    public async Task<IReadOnlyList<GitHubRepoEntry>> ListReposAsync(
        string accessToken,
        string affiliation,
        int perPage,
        int maxRepos,
        CancellationToken ct
    )
    {
        if (perPage <= 0 || perPage > 100)
            perPage = 100;
        if (maxRepos <= 0)
            maxRepos = 1000;

        // Use the rate-limit-aware "github" client so the 1k-repo cap doesn't burn the bucket.
        HttpClient http = _httpClientFactory.CreateClient("github");
        List<GitHubRepoEntry> repos = [];

        string? nextUrl =
            "https://api.github.com/user/repos?per_page="
            + perPage
            + "&affiliation="
            + Uri.EscapeDataString(affiliation)
            + "&sort=full_name";

        while (!string.IsNullOrEmpty(nextUrl) && repos.Count < maxRepos)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
            request.Headers.Accept.Add(new("application/vnd.github+json"));

            using HttpResponseMessage response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            using JsonDocument doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct),
                cancellationToken: ct
            );
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                break;

            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                GitHubRepoEntry? entry = ParseRepoEntry(element);
                if (entry is null)
                    continue;
                repos.Add(entry);
                if (repos.Count >= maxRepos)
                    break;
            }

            nextUrl = TryExtractNextLink(response);
        }

        return repos;
    }

    private static GitHubRepoEntry? ParseRepoEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        long id =
            element.TryGetProperty("id", out JsonElement idEl)
            && idEl.TryGetInt64(out long parsedId)
                ? parsedId
                : 0;
        string name = element.TryGetProperty("name", out JsonElement nameEl)
            ? nameEl.GetString() ?? string.Empty
            : string.Empty;
        string fullName = element.TryGetProperty("full_name", out JsonElement fullEl)
            ? fullEl.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(name))
            return null;

        // Owner is "<login>/<repo>" — split rather than fish through owner.login so a fork's
        // owner field is consistent with full_name regardless of org-vs-user.
        int slash = fullName.IndexOf('/');
        string owner = slash > 0 ? fullName[..slash] : string.Empty;
        if (string.IsNullOrEmpty(owner))
            return null;

        string? description =
            element.TryGetProperty("description", out JsonElement descEl)
            && descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;
        string? defaultBranch =
            element.TryGetProperty("default_branch", out JsonElement branchEl)
            && branchEl.ValueKind == JsonValueKind.String
                ? branchEl.GetString()
                : null;
        bool isPrivate =
            element.TryGetProperty("private", out JsonElement privEl) && privEl.GetBoolean();
        bool archived =
            element.TryGetProperty("archived", out JsonElement archEl) && archEl.GetBoolean();
        bool fork = element.TryGetProperty("fork", out JsonElement forkEl) && forkEl.GetBoolean();
        string? language =
            element.TryGetProperty("language", out JsonElement langEl)
            && langEl.ValueKind == JsonValueKind.String
                ? langEl.GetString()
                : null;

        return new(
            id,
            owner,
            name,
            fullName,
            description,
            defaultBranch,
            isPrivate,
            archived,
            fork,
            language
        );
    }

    // RFC 5988 Link header: <url>; rel="next", <url>; rel="last". Pick the rel="next" url.
    private static string? TryExtractNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? values))
            return null;
        foreach (string headerValue in values)
        {
            foreach (string part in headerValue.Split(','))
            {
                string segment = part.Trim();
                int relIdx = segment.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase);
                if (relIdx < 0)
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

    public async Task<OAuthSigninResult> ExchangeCodeForSigninAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    )
    {
        OAuthTokenSnapshot snapshot = await ExchangeCodeAsync(config, code, codeVerifier, ct);
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        string? email = await ProbePrimaryEmailAsync(http, snapshot.AccessToken, ct);
        // AccountId is the numeric GitHub user id; AccountLogin is the handle.
        return new(
            Subject: snapshot.AccountId ?? string.Empty,
            Login: snapshot.AccountLogin,
            Email: email,
            Token: snapshot
        );
    }

    // The primary email is only on /user when scope includes user:email; fall back to /user/emails.
    private static async Task<string?> ProbePrimaryEmailAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://api.github.com/user/emails"
        );
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            bool primary =
                entry.TryGetProperty("primary", out JsonElement primaryEl)
                && primaryEl.GetBoolean();
            bool verified =
                entry.TryGetProperty("verified", out JsonElement verifiedEl)
                && verifiedEl.GetBoolean();
            if (!primary || !verified)
                continue;
            return entry.TryGetProperty("email", out JsonElement emailEl)
                ? emailEl.GetString()
                : null;
        }
        return null;
    }

    private sealed class GitHubTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
