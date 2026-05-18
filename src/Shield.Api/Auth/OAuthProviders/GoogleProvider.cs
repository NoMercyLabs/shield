using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shield.Api.Auth.OAuthProviders;

// Google OAuth 2.0 with PKCE. Mail-scope only — Shield uses XOAUTH2 against gmail SMTP.
// Refresh tokens are only issued when access_type=offline+prompt=consent are passed.
public sealed class GoogleProvider : IOAuthProvider
{
    public const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string UserInfoUrl = "https://openidconnect.googleapis.com/v1/userinfo";

    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public OAuthProvider Provider => OAuthProvider.Google;
    public string DefaultScopes => "https://mail.google.com/ openid email profile";

    // Signin doesn't need Gmail SMTP scope — keep it to the OpenID claims.
    public string SigninDefaultScopes => "openid email profile";
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
            ["scope"] = scopes,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
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

        GoogleTokenResponse? body = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            throw new InvalidOperationException("Google returned no access token.");

        (string email, string id) = await ProbeUserAsync(http, body.AccessToken, ct);

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return new(
            OAuthProvider.Google,
            body.AccessToken,
            body.RefreshToken,
            expiresAt,
            body.Scope ?? string.Empty,
            email,
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

        GoogleTokenResponse? body = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
            cancellationToken: ct
        );
        if (body is null || string.IsNullOrEmpty(body.AccessToken))
            return null;

        DateTime? expiresAt =
            body.ExpiresIn > 0 ? DateTime.UtcNow.AddSeconds(body.ExpiresIn) : null;

        return current with
        {
            AccessToken = body.AccessToken,
            // Google reuses the refresh token unless rotated; keep the prior value when no new one.
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
                RevokeUrl + "?token=" + Uri.EscapeDataString(token.AccessToken),
                content: null,
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
        // ExchangeCodeAsync already probes /userinfo — AccountLogin holds the email, AccountId is the sub.
        return new(
            Subject: snapshot.AccountId ?? string.Empty,
            Login: snapshot.AccountLogin,
            Email: snapshot.AccountLogin,
            Token: snapshot
        );
    }

    private static async Task<(string email, string id)> ProbeUserAsync(
        HttpClient http,
        string accessToken,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new("Bearer", accessToken);
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return ("(unknown)", "");

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        string email = root.TryGetProperty("email", out JsonElement emailEl)
            ? emailEl.GetString() ?? "(unknown)"
            : "(unknown)";
        string id = root.TryGetProperty("sub", out JsonElement subEl)
            ? subEl.GetString() ?? ""
            : "";
        return (email, id);
    }

    private sealed class GoogleTokenResponse
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
