using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Http;

namespace Shield.Api.Auth.OAuthProviders;

// Bitbucket Cloud OAuth 2.0. Uses HTTP Basic for the token exchange (RFC 6749 §2.3.1)
// and paginates with the `next` field in the response body (NOT RFC 5988 Link). Bitbucket
// doesn't support PKCE for "OAuth Consumers" — they use a fixed client-secret model.
public sealed class BitbucketProvider : IOAuthProvider
{
    public const string AuthorizeUrl = "https://bitbucket.org/site/oauth2/authorize";
    public const string TokenUrl = "https://bitbucket.org/site/oauth2/access_token";
    public const string UserUrl = "https://api.bitbucket.org/2.0/user";
    public const string EmailsUrl = "https://api.bitbucket.org/2.0/user/emails";
    public const string ReposUrl = "https://api.bitbucket.org/2.0/repositories?role=member";

    private readonly IHttpClientFactory _httpClientFactory;

    public BitbucketProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public OAuthProvider Provider => OAuthProvider.Bitbucket;
    public string DefaultScopes => "account repository email";
    public string SigninDefaultScopes => "account email";

    // Bitbucket OAuth Consumers don't support PKCE — client_secret is required in the
    // exchange. The interface flag lets the framework decide whether to send the
    // code_challenge in the authorize URL.
    public bool SupportsPkce => false;

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
            ["response_type"] = "code",
            ["state"] = state,
            // Bitbucket reads scopes from the consumer config, not the URL — passing them
            // here is ignored, but kept for parity with the IOAuthProvider contract.
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
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                }
            ),
        };
        string basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")
        );
        request.Headers.Authorization = new("Basic", basic);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        BitbucketTokenResponse? body =
            await response.Content.ReadFromJsonAsync<BitbucketTokenResponse>(cancellationToken: ct);
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            throw new InvalidOperationException("Bitbucket returned no access token.");

        (string username, string uuid) = await ProbeUserAsync(http, body.AccessToken, ct);

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return new(
            OAuthProvider.Bitbucket,
            body.AccessToken,
            body.RefreshToken,
            expiresAt,
            body.Scopes ?? string.Empty,
            username,
            uuid,
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
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                }
            ),
        };
        string basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")
        );
        request.Headers.Authorization = new("Basic", basic);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        BitbucketTokenResponse? body =
            await response.Content.ReadFromJsonAsync<BitbucketTokenResponse>(cancellationToken: ct);
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

    public Task RevokeAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot token,
        CancellationToken ct
    )
    {
        // Bitbucket doesn't expose a public revoke endpoint for OAuth Consumer tokens —
        // tokens expire naturally or via the user's app-management page. Local-row delete
        // is the operator-visible action.
        return Task.CompletedTask;
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
        int maxRepos = options.MaxRepositories <= 0 ? 1000 : options.MaxRepositories;
        int perPage = options.PerPage is <= 0 or > 100 ? 100 : options.PerPage;

        HttpClient http = _httpClientFactory.CreateClient("oauth");
        List<RepositorySummary> repos = [];
        string? nextUrl = ReposUrl + "&pagelen=" + perPage;

        while (!string.IsNullOrEmpty(nextUrl) && repos.Count < maxRepos)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

            using HttpResponseMessage response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            BitbucketRepoPage? page = await response.Content.ReadFromJsonAsync<BitbucketRepoPage>(
                cancellationToken: ct
            );
            if (page?.Values is null)
                break;

            foreach (BitbucketRepoEntry entry in page.Values)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                    continue;
                int slash = entry.FullName.IndexOf('/');
                string owner = slash > 0 ? entry.FullName[..slash] : string.Empty;
                string name = slash > 0 ? entry.FullName[(slash + 1)..] : entry.FullName;
                if (string.IsNullOrEmpty(owner))
                    continue;
                repos.Add(
                    new(
                        Owner: owner,
                        Name: name,
                        FullName: entry.FullName,
                        Description: entry.Description,
                        DefaultBranch: entry.MainBranch?.Name,
                        IsPrivate: entry.IsPrivate,
                        Archived: false,
                        Fork: entry.Parent is not null,
                        Language: entry.Language
                    )
                );
                if (repos.Count >= maxRepos)
                    break;
            }

            nextUrl = page.Next;
        }
        return repos;
    }

    private static async Task<(string username, string uuid)> ProbeUserAsync(
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
        string uuid = root.TryGetProperty("uuid", out JsonElement uuidEl)
            ? uuidEl.GetString() ?? ""
            : "";
        return (username, uuid);
    }

    private static async Task<string?> ProbePrimaryEmailAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, EmailsUrl);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        if (!doc.RootElement.TryGetProperty("values", out JsonElement values))
            return null;
        foreach (JsonElement entry in values.EnumerateArray())
        {
            bool isPrimary =
                entry.TryGetProperty("is_primary", out JsonElement primaryEl)
                && primaryEl.GetBoolean();
            bool isConfirmed =
                entry.TryGetProperty("is_confirmed", out JsonElement confirmedEl)
                && confirmedEl.GetBoolean();
            if (!isPrimary || !isConfirmed)
                continue;
            return entry.TryGetProperty("email", out JsonElement emailEl)
                ? emailEl.GetString()
                : null;
        }
        return null;
    }

    private sealed class BitbucketTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scopes")]
        public string? Scopes { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private sealed class BitbucketRepoPage
    {
        [JsonPropertyName("values")]
        public List<BitbucketRepoEntry>? Values { get; set; }

        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }

    private sealed class BitbucketRepoEntry
    {
        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("is_private")]
        public bool IsPrivate { get; set; }

        [JsonPropertyName("mainbranch")]
        public BitbucketBranchRef? MainBranch { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("parent")]
        public object? Parent { get; set; }
    }

    private sealed class BitbucketBranchRef
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
