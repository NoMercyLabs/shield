using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[NoApiToken]
public sealed class SettingsController : ControllerBase
{
    public static class Keys
    {
        public const string SingleUserMode = "singleUserMode";
        public const string OpenApiEnabled = "openApiEnabled";
        public const string OidcEnabled = "oidcEnabled";
        public const string OidcIssuer = "oidcIssuer";
        public const string OidcClientId = "oidcClientId";
        public const string OidcClientSecret = "oidcClientSecret";
        public const string AlertSeverityFloor = "alertSeverityFloor";
        public const string RetentionDays = "retentionDays";

        // Mirrors AppSettingKeys.OAuthRedirectBase ("oauth.redirectBase") — kept in sync so
        // BuildResponse + Update read/write the same row that OAuthController consumes.
        public const string OAuthRedirectBase = "oauth.redirectBase";
    }

    // Toggles that flip middleware/OpenApi pipeline at boot; runtime change requires restart.
    private static readonly string[] RestartRequiredKeys =
    [
        Keys.SingleUserMode,
        Keys.OpenApiEnabled,
    ];

    private readonly ShieldDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _appSettings;

    public SettingsController(
        ShieldDbContext db,
        IDataProtectionProvider protectionProvider,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IAppSettingsService appSettings,
        IHttpClientFactory httpClientFactory
    )
    {
        _db = db;
        _protector = protectionProvider.CreateProtector("shield.settings");
        _configuration = configuration;
        _environment = environment;
        _appSettings = appSettings;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken ct)
    {
        Dictionary<string, string> stored = await LoadAllAsync(ct);
        return Ok(BuildResponse(stored));
    }

    [HttpPut]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [RequireOriginalIdentity]
    public async Task<ActionResult<UpdateSettingsResponse>> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken ct
    )
    {
        Dictionary<string, string> existing = await LoadAllAsync(ct);
        Dictionary<string, string> updated = new(StringComparer.Ordinal);

        updated[Keys.SingleUserMode] = request.SingleUserMode ? "true" : "false";
        updated[Keys.OpenApiEnabled] = request.OpenApiEnabled ? "true" : "false";
        updated[Keys.OidcEnabled] = request.OidcEnabled ? "true" : "false";
        updated[Keys.OidcIssuer] = request.OidcIssuer ?? "";
        updated[Keys.OidcClientId] = request.OidcClientId ?? "";
        updated[Keys.AlertSeverityFloor] = request.AlertSeverityFloor.ToString();
        updated[Keys.RetentionDays] = request.RetentionDays.ToString();
        updated[Keys.OAuthRedirectBase] = request.OAuthRedirectBase ?? "";

        // Only overwrite the secret when caller supplies a non-empty value; otherwise preserve it.
        if (!string.IsNullOrEmpty(request.OidcClientSecret))
            updated[Keys.OidcClientSecret] = request.OidcClientSecret;

        ApplyProviderPatch(
            updated,
            request.Github,
            AppSettingKeys.GithubOAuthClientId,
            AppSettingKeys.GithubOAuthClientSecret,
            AppSettingKeys.GithubOAuthScopes
        );
        ApplyProviderPatch(
            updated,
            request.Slack,
            AppSettingKeys.SlackOAuthClientId,
            AppSettingKeys.SlackOAuthClientSecret,
            AppSettingKeys.SlackOAuthScopes
        );
        ApplyProviderPatch(
            updated,
            request.Google,
            AppSettingKeys.GoogleOAuthClientId,
            AppSettingKeys.GoogleOAuthClientSecret,
            AppSettingKeys.GoogleOAuthScopes
        );

        List<string> restartKeys = [];
        Guid? updatedBy = ResolveUserId();
        DateTime now = DateTime.UtcNow;

        foreach ((string key, string value) in updated)
        {
            existing.TryGetValue(key, out string? previous);
            if (previous == value)
                continue;

            AppSetting? row = await _db.AppSettings.FirstOrDefaultAsync(
                item => item.Key == key,
                ct
            );
            string encrypted = _protector.Protect(value);
            if (row is null)
            {
                _db.AppSettings.Add(
                    new()
                    {
                        Key = key,
                        ValueEncrypted = encrypted,
                        UpdatedAt = now,
                        UpdatedBy = updatedBy,
                    }
                );
            }
            else
            {
                row.ValueEncrypted = encrypted;
                row.UpdatedAt = now;
                row.UpdatedBy = updatedBy;
            }

            if (RestartRequiredKeys.Contains(key))
                restartKeys.Add(key);
        }

        await _db.SaveChangesAsync(ct);
        // Bust the AppSettingsService cache so OAuthController sees the new creds without restart.
        await _appSettings.ReloadAsync(ct);

        Dictionary<string, string> fresh = await LoadAllAsync(ct);
        return Ok(
            new UpdateSettingsResponse(BuildResponse(fresh), restartKeys.Count > 0, restartKeys)
        );
    }

    private static void ApplyProviderPatch(
        Dictionary<string, string> updated,
        OAuthProviderConfigPatch? patch,
        string clientIdKey,
        string secretKey,
        string scopesKey
    )
    {
        if (patch is null)
            return;
        if (patch.ClientId is not null)
            updated[clientIdKey] = patch.ClientId;
        if (patch.Scopes is not null)
            updated[scopesKey] = patch.Scopes;
        // null = preserve; "" = clear; non-empty = overwrite.
        if (patch.ClientSecret is not null)
            updated[secretKey] = patch.ClientSecret;
    }

    [HttpPost("test-oidc")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<TestOidcResponse>> TestOidc(
        [FromBody] TestOidcRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Issuer))
            return Ok(new TestOidcResponse(false, "Issuer URL is required."));
        if (!Uri.TryCreate(request.Issuer, UriKind.Absolute, out Uri? issuerUri))
            return Ok(new TestOidcResponse(false, "Issuer URL is not a valid absolute URI."));

        string discoveryUrl =
            issuerUri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            + "/.well-known/openid-configuration";

        try
        {
            HttpClient client = _httpClientFactory.CreateClient("oidc-test");
            client.Timeout = TimeSpan.FromSeconds(8);
            HttpResponseMessage response = await client.GetAsync(discoveryUrl, ct);
            if (!response.IsSuccessStatusCode)
                return Ok(
                    new TestOidcResponse(
                        false,
                        $"Discovery endpoint returned {(int)response.StatusCode}."
                    )
                );

            await using Stream body = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            JsonElement root = doc.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("issuer", out _)
                || !root.TryGetProperty("authorization_endpoint", out _)
                || !root.TryGetProperty("token_endpoint", out _)
            )
            {
                return Ok(
                    new TestOidcResponse(false, "Discovery response missing required fields.")
                );
            }

            return Ok(new TestOidcResponse(true, null));
        }
        catch (TaskCanceledException)
        {
            return Ok(new TestOidcResponse(false, "Discovery request timed out."));
        }
        catch (HttpRequestException ex)
        {
            return Ok(new TestOidcResponse(false, ex.Message));
        }
        catch (JsonException)
        {
            return Ok(new TestOidcResponse(false, "Discovery response was not valid JSON."));
        }
    }

    [HttpGet("runtime")]
    public ActionResult<RuntimeInfoResponse> Runtime()
    {
        string version =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? typeof(SettingsController).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return Ok(
            new RuntimeInfoResponse(
                version,
                _environment.EnvironmentName,
                _environment.ContentRootPath,
                _environment.WebRootPath ?? ""
            )
        );
    }

    private async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct)
    {
        List<AppSetting> rows = await _db.AppSettings.ToListAsync(ct);
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (AppSetting row in rows)
        {
            try
            {
                map[row.Key] = _protector.Unprotect(row.ValueEncrypted);
            }
            catch
            {
                // Unreadable row (rotated key/corrupt) — fall back to empty so the page still loads.
                map[row.Key] = "";
            }
        }
        return map;
    }

    private SettingsResponse BuildResponse(Dictionary<string, string> stored)
    {
        bool singleUser = ReadBool(
            stored,
            Keys.SingleUserMode,
            _configuration.GetValue("Shield:SingleUser", false)
        );
        bool openApi = ReadBool(
            stored,
            Keys.OpenApiEnabled,
            _configuration.GetValue("Shield:OpenApi:Enabled", false)
        );
        bool oidcEnabled = ReadBool(
            stored,
            Keys.OidcEnabled,
            _configuration.GetValue("Shield:Oidc:Enabled", false)
        );

        string? issuer =
            ReadString(stored, Keys.OidcIssuer) ?? _configuration["Shield:Oidc:Issuer"];
        string? clientId =
            ReadString(stored, Keys.OidcClientId) ?? _configuration["Shield:Oidc:ClientId"];
        string? secret = ReadString(stored, Keys.OidcClientSecret);
        string? secretMasked = string.IsNullOrEmpty(secret) ? null : MaskSecret(secret);

        Severity floor = Severity.Low;
        if (
            stored.TryGetValue(Keys.AlertSeverityFloor, out string? floorValue)
            && Enum.TryParse(floorValue, ignoreCase: true, out Severity parsed)
        )
        {
            floor = parsed;
        }

        int retention = 90;
        if (
            stored.TryGetValue(Keys.RetentionDays, out string? retentionValue)
            && int.TryParse(retentionValue, out int parsedDays)
        )
        {
            retention = parsedDays;
        }

        OAuthProviderConfigResponse github = BuildProviderResponse(
            stored,
            AppSettingKeys.GithubOAuthClientId,
            AppSettingKeys.GithubOAuthClientSecret,
            AppSettingKeys.GithubOAuthScopes,
            _configuration["Shield:OAuth:Github:ClientId"]
        );
        OAuthProviderConfigResponse slack = BuildProviderResponse(
            stored,
            AppSettingKeys.SlackOAuthClientId,
            AppSettingKeys.SlackOAuthClientSecret,
            AppSettingKeys.SlackOAuthScopes,
            _configuration["Shield:OAuth:Slack:ClientId"]
        );
        OAuthProviderConfigResponse google = BuildProviderResponse(
            stored,
            AppSettingKeys.GoogleOAuthClientId,
            AppSettingKeys.GoogleOAuthClientSecret,
            AppSettingKeys.GoogleOAuthScopes,
            _configuration["Shield:OAuth:Google:ClientId"]
        );

        string? redirectBase = ReadString(stored, Keys.OAuthRedirectBase);

        return new(
            singleUser,
            openApi,
            oidcEnabled,
            string.IsNullOrEmpty(issuer) ? null : issuer,
            string.IsNullOrEmpty(clientId) ? null : clientId,
            secretMasked,
            floor,
            retention,
            github,
            slack,
            google,
            OAuthRedirectBase: string.IsNullOrEmpty(redirectBase) ? null : redirectBase
        );
    }

    private static OAuthProviderConfigResponse BuildProviderResponse(
        Dictionary<string, string> stored,
        string clientIdKey,
        string secretKey,
        string scopesKey,
        string? clientIdFallback
    )
    {
        string? clientId = ReadString(stored, clientIdKey) ?? clientIdFallback;
        string? secret = ReadString(stored, secretKey);
        string? scopes = ReadString(stored, scopesKey);
        string? secretMasked = string.IsNullOrEmpty(secret) ? null : MaskProviderSecret(secret);
        bool configured = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(secret);
        return new(
            string.IsNullOrEmpty(clientId) ? null : clientId,
            secretMasked,
            string.IsNullOrEmpty(scopes) ? null : scopes,
            configured
        );
    }

    // Provider-config mask is "****<last4>" for parity with the SettingsView display.
    private static string MaskProviderSecret(string secret) =>
        secret.Length <= 4 ? "****" : "****" + secret[^4..];

    private static bool ReadBool(Dictionary<string, string> map, string key, bool fallback)
    {
        if (map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed))
            return parsed;
        return fallback;
    }

    private static string? ReadString(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    private static string MaskSecret(string secret)
    {
        if (secret.Length <= 4)
            return new('•', 8);
        return new string('•', 8) + secret[^4..];
    }

    private Guid? ResolveUserId()
    {
        string? raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out Guid id) ? id : null;
    }
}
