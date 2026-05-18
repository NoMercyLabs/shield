using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Http;

namespace Shield.Api.Auth.OAuthProviders;

// GitLab.com OAuth 2.0 with PKCE. Self-hosted GitLab instances can override the host
// via OAuthClientConfig's redirect URI inference; the rest of the URL shapes are
// version-stable across community + enterprise editions.
public sealed class GitlabProvider : IOAuthProvider
{
    public const string AuthorizeUrl = "https://gitlab.com/oauth/authorize";
    public const string TokenUrl = "https://gitlab.com/oauth/token";
    public const string RevokeUrl = "https://gitlab.com/oauth/revoke";
    public const string UserUrl = "https://gitlab.com/api/v4/user";

    private readonly IHttpClientFactory _httpClientFactory;

    public GitlabProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public OAuthProvider Provider => OAuthProvider.Gitlab;

    // read_api gives us /projects?membership=true for the picker; read_repository lets us
    // fetch lockfile contents during scans. read_user is needed for the post-auth identity
    // probe.
    public string DefaultScopes => "read_user read_api read_repository";

    public string SigninDefaultScopes => "read_user openid email";
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
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
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
        using HttpResponseMessage response = await http.PostAsync(
            TokenUrl,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["client_secret"] = config.ClientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = config.RedirectUri,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = codeVerifier,
                }
            ),
            ct
        );
        response.EnsureSuccessStatusCode();

        GitlabTokenResponse? body = await response.Content.ReadFromJsonAsync<GitlabTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            throw new InvalidOperationException("GitLab returned no access token.");

        (string username, string id) = await ProbeUserAsync(http, body.AccessToken, ct);

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return new(
            OAuthProvider.Gitlab,
            body.AccessToken,
            body.RefreshToken,
            expiresAt,
            body.Scope ?? string.Empty,
            username,
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
        using HttpResponseMessage response = await http.PostAsync(
            TokenUrl,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["client_secret"] = config.ClientSecret,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                }
            ),
            ct
        );
        if (!response.IsSuccessStatusCode)
            return null;

        GitlabTokenResponse? body = await response.Content.ReadFromJsonAsync<GitlabTokenResponse>(
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
        try
        {
            using HttpResponseMessage response = await http.PostAsync(
                RevokeUrl,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["client_secret"] = config.ClientSecret,
                        ["token"] = token.AccessToken,
                    }
                ),
                ct
            );
        }
        catch
        {
            // Best-effort.
        }
    }

    public string BuildSigninAuthorizationUrl(
        OAuthClientConfig config,
        string state,
        string codeChallenge,
        string scopes
    ) => BuildAuthorizationUrl(config, state, codeChallenge, scopes);

    public async Task<OAuthSigninResult> ExchangeCodeForSigninAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    )
    {
        OAuthTokenSnapshot snapshot = await ExchangeCodeAsync(config, code, codeVerifier, ct);
        // GitLab's /user returns email when scope includes read_user (it does by default).
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        string? email = await ProbePrimaryEmailAsync(http, snapshot.AccessToken, ct);
        return new(
            Subject: snapshot.AccountId ?? string.Empty,
            Login: snapshot.AccountLogin,
            Email: email,
            Token: snapshot
        );
    }

    public async Task<IReadOnlyList<RepositorySummary>?> ListRepositoriesAsync(
        string accessToken,
        RepositoryListOptions options,
        CancellationToken ct
    )
    {
        IReadOnlyList<GitlabProjectEntry> projects = await ListProjectsAsync(
            accessToken,
            options.PerPage,
            options.MaxRepositories,
            ct
        );
        List<RepositorySummary> normalised = new(projects.Count);
        foreach (GitlabProjectEntry project in projects)
        {
            int slash = project.PathWithNamespace.IndexOf('/');
            string owner = slash > 0 ? project.PathWithNamespace[..slash] : string.Empty;
            string name = slash > 0 ? project.PathWithNamespace[(slash + 1)..] : project.Name;
            if (string.IsNullOrEmpty(owner))
                continue;
            normalised.Add(
                new(
                    Owner: owner,
                    Name: name,
                    FullName: project.PathWithNamespace,
                    Description: project.Description,
                    DefaultBranch: project.DefaultBranch,
                    IsPrivate: !string.Equals(
                        project.Visibility,
                        "public",
                        StringComparison.OrdinalIgnoreCase
                    ),
                    Archived: project.Archived,
                    // GitLab's simple project payload doesn't surface fork-of in one call;
                    // call sites that care can fetch /projects/{id}/forks. Default false.
                    Fork: false,
                    Language: null
                )
            );
        }
        return normalised;
    }

    public async Task<IReadOnlyList<GitlabProjectEntry>> ListProjectsAsync(
        string accessToken,
        int perPage,
        int maxProjects,
        CancellationToken ct
    )
    {
        if (perPage <= 0 || perPage > 100)
            perPage = 100;
        if (maxProjects <= 0)
            maxProjects = 1000;

        HttpClient http = _httpClientFactory.CreateClient("oauth");
        List<GitlabProjectEntry> projects = [];

        string? nextUrl =
            "https://gitlab.com/api/v4/projects?membership=true&simple=true&order_by=path&sort=asc"
            + "&per_page="
            + perPage;

        while (!string.IsNullOrEmpty(nextUrl) && projects.Count < maxProjects)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

            using HttpResponseMessage response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            List<GitlabProjectEntry>? page = await response.Content.ReadFromJsonAsync<
                List<GitlabProjectEntry>
            >(cancellationToken: ct);
            if (page is null || page.Count == 0)
                break;

            foreach (GitlabProjectEntry entry in page)
            {
                projects.Add(entry);
                if (projects.Count >= maxProjects)
                    break;
            }

            // GitLab uses Link header (RFC 5988) like GitHub. Newer instances also expose
            // X-Next-Page; the Link header is the cross-version safe path.
            nextUrl = TryExtractNextLink(response);
        }

        return projects;
    }

    private static async Task<(string username, string id)> ProbeUserAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserUrl);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("(unknown)", "");

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        string username = root.TryGetProperty("username", out JsonElement usernameEl)
            ? usernameEl.GetString() ?? "(unknown)"
            : "(unknown)";
        string id = root.TryGetProperty("id", out JsonElement idEl)
            ? idEl.GetRawText()
            : string.Empty;
        return (username, id);
    }

    private static async Task<string?> ProbePrimaryEmailAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        // GitLab's /user response includes "email" directly when read_user is granted.
        using HttpRequestMessage request = new(HttpMethod.Get, UserUrl);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        return doc.RootElement.TryGetProperty("email", out JsonElement emailEl)
            ? emailEl.GetString()
            : null;
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

    private sealed class GitlabTokenResponse
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

public sealed class GitlabProjectEntry
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path_with_namespace")]
    public string PathWithNamespace { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}
