using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shield.Core.Http;

namespace Shield.Api.Services.Auth;

// Talks to GitHub's OAuth Device Flow endpoints. Lives behind an interface so the
// controller doesn't bake github.com URLs into its tests, and so a fake can stand in
// during unit tests without spinning up WireMock.
public interface IGitHubDeviceFlowClient
{
    Task<GitHubDeviceCodeResponse> RequestDeviceCodeAsync(
        string clientId,
        string scopes,
        CancellationToken ct
    );

    Task<GitHubDeviceTokenResponse> PollAccessTokenAsync(
        string clientId,
        string deviceCode,
        CancellationToken ct
    );

    Task<GitHubUserProfile?> FetchUserProfileAsync(string accessToken, CancellationToken ct);
}

// Mirrors https://github.com/login/device/code response shape. Field names match the
// JSON wire payload so we can deserialize without explicit converter wiring.
public sealed record GitHubDeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval,
    // GitHub returns this for OAuth Apps with device flow enabled — the URL already carries
    // the user_code as a query param so github.com pre-fills the form. Always prefer this
    // for the "open verification page" link; fall back to VerificationUri only if absent.
    [property: JsonPropertyName("verification_uri_complete")] string? VerificationUriComplete = null
);

// Polling endpoint either returns access_token + scope on success, or an `error` code.
// We surface the raw fields and let the caller branch on AccessToken vs Error.
public sealed record GitHubDeviceTokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription
);

public sealed record GitHubUserProfile(string Login, string Id, string? AvatarUrl);

public sealed class GitHubDeviceFlowClient : IGitHubDeviceFlowClient
{
    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    private const string UserUrl = "https://api.github.com/user";

    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubDeviceFlowClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<GitHubDeviceCodeResponse> RequestDeviceCodeAsync(
        string clientId,
        string scopes,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Post, DeviceCodeUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = scopes }
            ),
        };
        request.Headers.Accept.Add(new("application/json"));

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        GitHubDeviceCodeResponse body =
            await response.Content.ReadFromJsonAsync<GitHubDeviceCodeResponse>(
                cancellationToken: ct
            )
            ?? throw new InvalidOperationException(
                "GitHub device-code endpoint returned an empty body."
            );
        return body;
    }

    public async Task<GitHubDeviceTokenResponse> PollAccessTokenAsync(
        string clientId,
        string deviceCode,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Post, AccessTokenUrl)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }
            ),
        };
        request.Headers.Accept.Add(new("application/json"));

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        // GitHub returns 200 even for `error: authorization_pending` so we don't throw on non-success
        // alone; callers branch on the parsed body. We do throw for hard transport / server errors.
        if (
            (int)response.StatusCode >= 500
            || response.StatusCode == HttpStatusCode.Unauthorized
            || response.StatusCode == HttpStatusCode.Forbidden
        )
        {
            response.EnsureSuccessStatusCode();
        }

        GitHubDeviceTokenResponse body =
            await response.Content.ReadFromJsonAsync<GitHubDeviceTokenResponse>(
                cancellationToken: ct
            )
            ?? throw new InvalidOperationException(
                "GitHub access-token endpoint returned an empty body."
            );
        return body;
    }

    public async Task<GitHubUserProfile?> FetchUserProfileAsync(
        string accessToken,
        CancellationToken ct
    )
    {
        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(HttpMethod.Get, UserUrl);
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
        JsonElement root = doc.RootElement;
        string login = root.TryGetProperty("login", out JsonElement loginEl)
            ? loginEl.GetString() ?? string.Empty
            : string.Empty;
        // id is numeric on the wire — read as raw text to avoid losing precision on very large ids.
        string id = root.TryGetProperty("id", out JsonElement idEl)
            ? idEl.GetRawText()
            : string.Empty;
        string? avatar =
            root.TryGetProperty("avatar_url", out JsonElement avatarEl)
            && avatarEl.ValueKind == JsonValueKind.String
                ? avatarEl.GetString()
                : null;
        return new(login, id, avatar);
    }
}
