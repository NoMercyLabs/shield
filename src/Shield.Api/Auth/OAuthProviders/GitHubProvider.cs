using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Api.Services;
using Shield.Core.Domain;

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
    public string SigninDefaultScopes => "read:user user:email";
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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

        return new OAuthTokenSnapshot(
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
        string url = string.Format(RevokeUrlTemplate, config.ClientId);
        using HttpRequestMessage request = new(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(new { access_token = token.AccessToken }),
        };
        string basic = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{config.ClientId}:{config.ClientSecret}")
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("shield-oauth");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );

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
        return new OAuthSigninResult(
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd("shield-oauth");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
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
