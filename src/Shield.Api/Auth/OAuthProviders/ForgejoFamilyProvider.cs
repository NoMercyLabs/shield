using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Shield.Core.Http;

namespace Shield.Api.Auth.OAuthProviders;

// Forgejo / Gitea / Codeberg share the exact same OAuth2 + REST API shapes (Forgejo is a
// hard fork of Gitea, Codeberg is a hosted Forgejo). One base class, three thin concrete
// subclasses that pin the upstream host. Codeberg is hardcoded; Forgejo and Gitea are
// configurable via `Shield:OAuth:<Provider>:Host` for self-hosted instances.
public abstract class ForgejoFamilyProvider : IOAuthProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri _hostBase;

    protected ForgejoFamilyProvider(IHttpClientFactory httpClientFactory, string hostUrl)
    {
        _httpClientFactory = httpClientFactory;
        _hostBase = new(hostUrl.TrimEnd('/') + "/");
    }

    public abstract OAuthProvider Provider { get; }

    public string DefaultScopes => "read:user read:repository";
    public string SigninDefaultScopes => "read:user openid email";
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
        return new Uri(_hostBase, "login/oauth/authorize").ToString() + "?" + UrlForm.Encode(query);
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
            new Uri(_hostBase, "login/oauth/access_token"),
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

        ForgejoTokenResponse? body = await response.Content.ReadFromJsonAsync<ForgejoTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            throw new InvalidOperationException($"{Provider} returned no access token.");

        (string username, string id) = await ProbeUserAsync(http, body.AccessToken, ct);

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return new(
            Provider,
            body.AccessToken,
            body.RefreshToken,
            expiresAt,
            string.Empty,
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
            new Uri(_hostBase, "login/oauth/access_token"),
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
        ForgejoTokenResponse? body = await response.Content.ReadFromJsonAsync<ForgejoTokenResponse>(
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

    public Task RevokeAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot token,
        CancellationToken ct
    )
    {
        // Forgejo / Gitea have no public revoke endpoint; tokens expire naturally.
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
        string? email = await ProbeEmailAsync(http, snapshot.AccessToken, ct);
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
        int perPage = options.PerPage is <= 0 or > 50 ? 50 : options.PerPage;

        HttpClient http = _httpClientFactory.CreateClient("oauth");
        List<RepositorySummary> repos = [];
        int page = 1;

        while (repos.Count < maxRepos)
        {
            Uri url = new(_hostBase, $"api/v1/user/repos?page={page}&limit={perPage}");
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new("token", accessToken);
            request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);

            using HttpResponseMessage response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            List<ForgejoRepoEntry>? body = await response.Content.ReadFromJsonAsync<
                List<ForgejoRepoEntry>
            >(cancellationToken: ct);
            if (body is null || body.Count == 0)
                break;

            foreach (ForgejoRepoEntry entry in body)
            {
                if (string.IsNullOrEmpty(entry.FullName))
                    continue;
                int slash = entry.FullName.IndexOf('/');
                string owner = slash > 0 ? entry.FullName[..slash] : string.Empty;
                string name = slash > 0 ? entry.FullName[(slash + 1)..] : entry.Name;
                if (string.IsNullOrEmpty(owner))
                    continue;
                repos.Add(
                    new(
                        Owner: owner,
                        Name: name,
                        FullName: entry.FullName,
                        Description: entry.Description,
                        DefaultBranch: entry.DefaultBranch,
                        IsPrivate: entry.IsPrivate,
                        Archived: entry.Archived,
                        Fork: entry.Fork,
                        Language: entry.Language
                    )
                );
                if (repos.Count >= maxRepos)
                    break;
            }

            // Forgejo paginates without a Link header by default; bail when a page comes
            // back smaller than the limit.
            if (body.Count < perPage)
                break;
            page++;
        }
        return repos;
    }

    private async Task<(string username, string id)> ProbeUserAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(_hostBase, "api/v1/user"));
        request.Headers.Authorization = new("token", accessToken);
        request.Headers.UserAgent.ParseAdd(ShieldUserAgent.Header);
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("(unknown)", "");
        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        string username = root.TryGetProperty("login", out JsonElement loginEl)
            ? loginEl.GetString() ?? "(unknown)"
            : "(unknown)";
        string id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetRawText() : "";
        return (username, id);
    }

    private async Task<string?> ProbeEmailAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(_hostBase, "api/v1/user"));
        request.Headers.Authorization = new("token", accessToken);
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

    private sealed class ForgejoTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private sealed class ForgejoRepoEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; set; }

        [JsonPropertyName("private")]
        public bool IsPrivate { get; set; }

        [JsonPropertyName("archived")]
        public bool Archived { get; set; }

        [JsonPropertyName("fork")]
        public bool Fork { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }
}

public sealed class CodebergProvider : ForgejoFamilyProvider
{
    public CodebergProvider(IHttpClientFactory httpClientFactory)
        : base(httpClientFactory, "https://codeberg.org") { }

    public override OAuthProvider Provider => OAuthProvider.Codeberg;
}

public sealed class ForgejoProvider : ForgejoFamilyProvider
{
    // Self-hosted instance — operator points Shield:OAuth:Forgejo:Host at their host.
    // Falls back to codeberg.org so the provider is at least bootable in dev.
    public ForgejoProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        : base(
            httpClientFactory,
            configuration["Shield:OAuth:Forgejo:Host"] ?? "https://codeberg.org"
        ) { }

    public override OAuthProvider Provider => OAuthProvider.Forgejo;
}

public sealed class GiteaProvider : ForgejoFamilyProvider
{
    public GiteaProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        : base(httpClientFactory, configuration["Shield:OAuth:Gitea:Host"] ?? "https://gitea.com")
    { }

    public override OAuthProvider Provider => OAuthProvider.Gitea;
}
