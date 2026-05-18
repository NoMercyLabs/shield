using System.Net.Http.Headers;
using System.Text.Json;
using Shield.Api.Services;
using Shield.Core.Domain;

namespace Shield.Api.Auth.OAuthProviders;

// Slack OAuth v2. We request bot scopes only (chat:write + channels:read); the bot
// token is what SlackOAuthChannel uses to post via chat.postMessage.
public sealed class SlackProvider : IOAuthProvider
{
    public const string AuthorizeUrl = "https://slack.com/oauth/v2/authorize";
    public const string TokenUrl = "https://slack.com/api/oauth.v2.access";
    public const string RevokeUrl = "https://slack.com/api/auth.revoke";

    // Sign-In-with-Slack uses the OIDC endpoints rather than the bot install flow.
    public const string SigninAuthorizeUrl = "https://slack.com/openid/connect/authorize";
    public const string SigninTokenUrl = "https://slack.com/api/openid.connect.token";
    public const string SigninUserInfoUrl = "https://slack.com/api/openid.connect.userInfo";

    private readonly IHttpClientFactory _httpClientFactory;

    public SlackProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public OAuthProvider Provider => OAuthProvider.Slack;
    public string DefaultScopes => "chat:write,channels:read,groups:read";
    public string SigninDefaultScopes => "openid email profile";
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
            ["redirect_uri"] = config.RedirectUri,
            ["state"] = state,
            ["scope"] = scopes,
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
                }
            ),
        };

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("ok", out JsonElement okElement) || !okElement.GetBoolean())
        {
            string err = root.TryGetProperty("error", out JsonElement e)
                ? e.GetString() ?? "unknown"
                : "unknown";
            throw new InvalidOperationException($"Slack oauth.v2.access failed: {err}");
        }

        string botAccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        string botUserId = root.TryGetProperty("bot_user_id", out JsonElement botEl)
            ? botEl.GetString() ?? ""
            : "";
        string teamName = root.TryGetProperty("team", out JsonElement teamEl)
            ? teamEl.TryGetProperty("name", out JsonElement nameEl)
                ? nameEl.GetString() ?? ""
                : ""
            : "";
        string teamId = root.TryGetProperty("team", out JsonElement teamEl2)
            ? teamEl2.TryGetProperty("id", out JsonElement idEl)
                ? idEl.GetString() ?? ""
                : ""
            : "";
        string scope = root.TryGetProperty("scope", out JsonElement scopeEl)
            ? scopeEl.GetString() ?? ""
            : "";

        return new(
            OAuthProvider.Slack,
            botAccessToken,
            null,
            null,
            scope,
            teamName,
            teamId,
            JsonSerializer.Serialize(new { botUserId, teamId })
        );
    }

    public Task<OAuthTokenSnapshot?> RefreshAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot current,
        CancellationToken ct
    )
    {
        // Slack bot tokens don't expire; no refresh flow needed.
        return Task.FromResult<OAuthTokenSnapshot?>(null);
    }

    public async Task RevokeAsync(
        OAuthClientConfig config,
        OAuthTokenSnapshot token,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Post, RevokeUrl);
        request.Headers.Authorization = new("Bearer", token.AccessToken);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
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
    )
    {
        Dictionary<string, string> query = new()
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["state"] = state,
            ["scope"] = scopes,
            ["response_type"] = "code",
        };
        return SigninAuthorizeUrl + "?" + UrlForm.Encode(query);
    }

    public async Task<OAuthSigninResult> ExchangeCodeForSigninAsync(
        OAuthClientConfig config,
        string code,
        string codeVerifier,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage tokenRequest = new(HttpMethod.Post, SigninTokenUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = config.ClientId,
                    ["client_secret"] = config.ClientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = config.RedirectUri,
                    ["grant_type"] = "authorization_code",
                }
            ),
        };
        using HttpResponseMessage tokenResponse = await http.SendAsync(tokenRequest, ct);
        tokenResponse.EnsureSuccessStatusCode();

        using JsonDocument tokenDoc = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement tokenRoot = tokenDoc.RootElement;
        if (!tokenRoot.TryGetProperty("ok", out JsonElement okEl) || !okEl.GetBoolean())
        {
            string err = tokenRoot.TryGetProperty("error", out JsonElement e)
                ? e.GetString() ?? "unknown"
                : "unknown";
            throw new InvalidOperationException($"Slack openid.connect.token failed: {err}");
        }

        string accessToken = tokenRoot.GetProperty("access_token").GetString() ?? string.Empty;
        string scope = tokenRoot.TryGetProperty("scope", out JsonElement scopeEl)
            ? scopeEl.GetString() ?? ""
            : "";

        using HttpRequestMessage userRequest = new(HttpMethod.Get, SigninUserInfoUrl);
        userRequest.Headers.Authorization = new("Bearer", accessToken);
        using HttpResponseMessage userResponse = await http.SendAsync(userRequest, ct);
        userResponse.EnsureSuccessStatusCode();

        using JsonDocument userDoc = await JsonDocument.ParseAsync(
            await userResponse.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement userRoot = userDoc.RootElement;
        string subject = userRoot.TryGetProperty("sub", out JsonElement subEl)
            ? subEl.GetString() ?? ""
            : "";
        string email = userRoot.TryGetProperty("email", out JsonElement emailEl)
            ? emailEl.GetString() ?? ""
            : "";
        string name = userRoot.TryGetProperty("name", out JsonElement nameEl)
            ? nameEl.GetString() ?? ""
            : email;

        OAuthTokenSnapshot snapshot = new(
            OAuthProvider.Slack,
            accessToken,
            null,
            null,
            scope,
            name,
            subject,
            null
        );
        return new(subject, name, string.IsNullOrEmpty(email) ? null : email, snapshot);
    }
}
