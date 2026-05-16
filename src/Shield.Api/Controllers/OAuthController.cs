using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shield.Api.Auth;
using Shield.Api.Auth.OAuthProviders;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/oauth")]
[Authorize]
public sealed class OAuthController : ControllerBase
{
    private readonly IAppSettingsService _settings;
    private readonly IOAuthStateStore _stateStore;
    private readonly IOAuthTokenStore _tokenStore;
    private readonly IOAuthProviderRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UserManager<ShieldUser> _userManager;
    private readonly SignInManager<ShieldUser> _signInManager;
    private readonly RoleManager<ShieldRole> _roleManager;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<OAuthController> _logger;

    private static readonly TimeSpan GithubReposCacheTtl = TimeSpan.FromMinutes(5);

    public OAuthController(
        IAppSettingsService settings,
        IOAuthStateStore stateStore,
        IOAuthTokenStore tokenStore,
        IOAuthProviderRegistry registry,
        IHttpClientFactory httpClientFactory,
        UserManager<ShieldUser> userManager,
        SignInManager<ShieldUser> signInManager,
        RoleManager<ShieldRole> roleManager,
        IMemoryCache memoryCache,
        ILogger<OAuthController> logger
    )
    {
        _settings = settings;
        _stateStore = stateStore;
        _tokenStore = tokenStore;
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    [HttpGet("{provider}/start")]
    [AllowAnonymous]
    public async Task<ActionResult<OAuthStartResponse>> Start(
        string provider,
        [FromQuery] string? intent,
        [FromQuery] string? redirect,
        CancellationToken ct
    )
    {
        if (!TryParseProvider(provider, out OAuthProvider parsedProvider))
            return BadRequest(new { error = $"Unknown provider '{provider}'" });

        if (!_registry.TryResolve(parsedProvider, out IOAuthProvider adapter))
            return BadRequest(new { error = $"No adapter registered for {parsedProvider}" });

        OAuthIntent parsedIntent = ParseIntent(intent);

        // Connect-flow keeps the prior Admin-only gate; signin must be anonymous.
        if (parsedIntent == OAuthIntent.Connect)
        {
            if (User.Identity?.IsAuthenticated != true || !User.IsInRole(ShieldRoles.Admin))
                return Forbid();
        }

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        if (
            !TryBuildClientConfig(
                snapshot,
                parsedProvider,
                out OAuthClientConfig? clientConfig,
                out string? err
            )
        )
            return BadRequest(new { error = err });

        string state = GenerateUrlToken();
        string codeVerifier = adapter.SupportsPkce ? GenerateUrlToken(48) : string.Empty;
        string codeChallenge = adapter.SupportsPkce
            ? Pkce.S256Challenge(codeVerifier)
            : string.Empty;

        string returnUrl = SanitizeRedirect(redirect, parsedIntent);

        _stateStore.Save(
            state,
            new OAuthStateEntry(
                parsedProvider,
                codeVerifier,
                returnUrl,
                DateTime.UtcNow + OAuthStateStore.StateTtl,
                parsedIntent
            )
        );

        string scopes = ResolveScopes(snapshot, parsedProvider, adapter, parsedIntent);
        string url =
            parsedIntent == OAuthIntent.Signin
                ? adapter.BuildSigninAuthorizationUrl(clientConfig!, state, codeChallenge, scopes)
                : adapter.BuildAuthorizationUrl(clientConfig!, state, codeChallenge, scopes);
        return Ok(new OAuthStartResponse(url, state));
    }

    [HttpGet("{provider}/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-burst")]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery(Name = "error")] string? oauthError,
        CancellationToken ct
    )
    {
        if (!TryParseProvider(provider, out OAuthProvider parsedProvider))
            return RedirectToReturn(null, OAuthIntent.Connect, "unknown_provider");

        if (!string.IsNullOrEmpty(oauthError))
            return RedirectToReturn(null, OAuthIntent.Connect, oauthError!);

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return RedirectToReturn(null, OAuthIntent.Connect, "missing_code_or_state");

        OAuthStateEntry? entry = _stateStore.Consume(state!);
        if (entry is null || entry.Provider != parsedProvider)
            return RedirectToReturn(null, OAuthIntent.Connect, "invalid_state");

        if (!_registry.TryResolve(parsedProvider, out IOAuthProvider adapter))
            return RedirectToReturn(entry, entry.Intent, "no_adapter");

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        if (
            !TryBuildClientConfig(
                snapshot,
                parsedProvider,
                out OAuthClientConfig? clientConfig,
                out string? err
            )
        )
            return RedirectToReturn(entry, entry.Intent, err ?? "config_missing");

        try
        {
            return entry.Intent == OAuthIntent.Signin
                ? await HandleSigninCallback(adapter, clientConfig!, code!, entry, ct)
                : await HandleConnectCallback(adapter, clientConfig!, code!, entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "OAuth callback failed for {Provider} intent {Intent}",
                parsedProvider,
                entry.Intent
            );
            return RedirectToReturn(entry, entry.Intent, "exchange_failed");
        }
    }

    private async Task<IActionResult> HandleConnectCallback(
        IOAuthProvider adapter,
        OAuthClientConfig clientConfig,
        string code,
        OAuthStateEntry entry,
        CancellationToken ct
    )
    {
        OAuthTokenSnapshot exchanged = await adapter.ExchangeCodeAsync(
            clientConfig,
            code,
            entry.CodeVerifier,
            ct
        );
        await _tokenStore.SaveAsync(exchanged, ct);
        return Redirect(
            "/settings?oauth=connected&provider="
                + Uri.EscapeDataString(adapter.Provider.ToString().ToLowerInvariant())
        );
    }

    private async Task<IActionResult> HandleSigninCallback(
        IOAuthProvider adapter,
        OAuthClientConfig clientConfig,
        string code,
        OAuthStateEntry entry,
        CancellationToken ct
    )
    {
        OAuthSigninResult result = await adapter.ExchangeCodeForSigninAsync(
            clientConfig,
            code,
            entry.CodeVerifier,
            ct
        );
        if (string.IsNullOrEmpty(result.Subject))
            return RedirectToReturn(entry, OAuthIntent.Signin, "oauth_signin_no_subject");

        // 1. Prior signin for this provider+subject — straight login.
        IntegrationTokenLookup? existingLink = await _tokenStore.FindBySubjectAsync(
            adapter.Provider,
            result.Subject,
            ct
        );
        ShieldUser? user = existingLink?.LinkedUserId is { } linkedId
            ? await _userManager.FindByIdAsync(linkedId.ToString())
            : null;

        if (user is null)
        {
            // 2. Existing local user with matching email — link this provider to them.
            if (!string.IsNullOrEmpty(result.Email))
                user = await _userManager.FindByEmailAsync(result.Email);
        }

        if (user is null)
        {
            // 3. No match — provision a new account if the policy allows.
            (ShieldUser? created, string? rejectReason) = await TryProvisionUserAsync(
                adapter.Provider,
                result,
                ct
            );
            if (created is null)
                return RedirectToReturn(
                    entry,
                    OAuthIntent.Signin,
                    rejectReason ?? "oauth_signin_rejected"
                );
            user = created;
        }

        await _tokenStore.SaveSigninAsync(result.Token, result.Subject, user.Id, ct);

        await _signInManager.SignInAsync(user, isPersistent: true);
        return Redirect(string.IsNullOrEmpty(entry.ReturnUrl) ? "/" : entry.ReturnUrl);
    }

    private async Task<(ShieldUser? user, string? reject)> TryProvisionUserAsync(
        OAuthProvider provider,
        OAuthSigninResult signin,
        CancellationToken ct
    )
    {
        // First real user wins the Admin role — synthetic single-user is excluded from the count.
        bool isFirstUser = !await _userManager.Users.AnyAsync(
            candidate => candidate.UserName != IdentitySeeder.SingleUserName,
            ct
        );

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        if (!isFirstUser && !snapshot.RegistrationOpen)
            return (null, "oauth_signin_rejected");

        string role = isFirstUser ? ShieldRoles.Admin : ShieldRoles.Viewer;
        if (!await _roleManager.RoleExistsAsync(role))
            await _roleManager.CreateAsync(new ShieldRole(role));

        // Identity's default allowed-username set is alphanumeric, so flatten ":" and "-" out
        // of `<provider>:<login>` rather than fight the policy at startup.
        string rawUsername = $"{provider.ToString().ToLowerInvariant()}{signin.Login}";
        string username = new(rawUsername.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(username))
            username = provider.ToString().ToLowerInvariant() + Guid.NewGuid().ToString("n")[..8];
        // Avoid clashing with an existing UserName (rare but possible on re-runs after a failed link).
        if (await _userManager.FindByNameAsync(username) is not null)
            username += Guid.NewGuid().ToString("n")[..6];

        ShieldUser user = new()
        {
            UserName = username,
            Email = signin.Email,
            EmailConfirmed = !string.IsNullOrEmpty(signin.Email),
            CreatedAt = DateTime.UtcNow,
        };
        IdentityResult create = await _userManager.CreateAsync(user, GenerateRandomPassword());
        if (!create.Succeeded)
        {
            _logger.LogWarning(
                "OAuth signin create failed: {Errors}",
                string.Join(", ", create.Errors.Select(error => error.Description))
            );
            return (null, "oauth_signin_create_failed");
        }
        IdentityResult assign = await _userManager.AddToRoleAsync(user, role);
        if (!assign.Succeeded)
        {
            _logger.LogWarning(
                "OAuth signin role assign failed: {Errors}",
                string.Join(", ", assign.Errors.Select(error => error.Description))
            );
        }
        return (user, null);
    }

    [HttpPost("{provider}/disconnect")]
    [Authorize(Roles = ShieldRoles.Admin)]
    public async Task<IActionResult> Disconnect(string provider, CancellationToken ct)
    {
        if (!TryParseProvider(provider, out OAuthProvider parsedProvider))
            return BadRequest(new { error = $"Unknown provider '{provider}'" });

        OAuthTokenSnapshot? existing = await _tokenStore.GetAsync(parsedProvider, ct);
        if (
            existing is not null
            && _registry.TryResolve(parsedProvider, out IOAuthProvider adapter)
        )
        {
            AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
            if (
                TryBuildClientConfig(
                    snapshot,
                    parsedProvider,
                    out OAuthClientConfig? clientConfig,
                    out _
                )
            )
                await adapter.RevokeAsync(clientConfig!, existing, ct);
        }
        await _tokenStore.DisconnectAsync(parsedProvider, ct);
        return NoContent();
    }

    [HttpGet("{provider}/status")]
    public async Task<ActionResult<OAuthStatusResponse>> Status(
        string provider,
        CancellationToken ct
    )
    {
        if (!TryParseProvider(provider, out OAuthProvider parsedProvider))
            return BadRequest(new { error = $"Unknown provider '{provider}'" });

        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(parsedProvider, ct);
        if (token is null)
        {
            return Ok(
                new OAuthStatusResponse(
                    parsedProvider,
                    Connected: false,
                    AccountLogin: null,
                    AccountId: null,
                    Scopes: null,
                    ExpiresAt: null,
                    UpdatedAt: null
                )
            );
        }
        return Ok(
            new OAuthStatusResponse(
                parsedProvider,
                Connected: true,
                token.AccountLogin,
                token.AccountId,
                token.Scopes,
                token.ExpiresAt,
                UpdatedAt: null
            )
        );
    }

    // Slack-specific: list channels the bot can post to. Used by the channel-create dropdown.
    [HttpGet("slack/channels")]
    public async Task<ActionResult<SlackChannelsResponse>> SlackChannels(CancellationToken ct)
    {
        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(OAuthProvider.Slack, ct);
        if (token is null)
            return BadRequest(new { error = "Slack is not connected" });

        HttpClient http = _httpClientFactory.CreateClient("oauth");
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://slack.com/api/conversations.list?exclude_archived=true&types=public_channel,private_channel&limit=200"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, new { error = "Slack API call failed" });

        using JsonDocument doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct
        );
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("ok", out JsonElement okEl) || !okEl.GetBoolean())
        {
            string err = root.TryGetProperty("error", out JsonElement errEl)
                ? errEl.GetString() ?? "unknown"
                : "unknown";
            return BadRequest(new { error = err });
        }

        List<SlackChannelInfo> channels = new();
        if (
            root.TryGetProperty("channels", out JsonElement channelsEl)
            && channelsEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement channel in channelsEl.EnumerateArray())
            {
                string id = channel.TryGetProperty("id", out JsonElement idEl)
                    ? idEl.GetString() ?? ""
                    : "";
                string name = channel.TryGetProperty("name", out JsonElement nameEl)
                    ? nameEl.GetString() ?? ""
                    : "";
                bool isPrivate =
                    channel.TryGetProperty("is_private", out JsonElement privEl)
                    && privEl.GetBoolean();
                if (!string.IsNullOrEmpty(id))
                    channels.Add(new SlackChannelInfo(id, name, isPrivate));
            }
        }
        return Ok(new SlackChannelsResponse(channels));
    }

    // GitHub-specific: list the connected user's repos for the "Pick from GitHub" Sources picker.
    // Cached 5min per (user, affiliation) so the modal can be reopened cheaply.
    [HttpGet("github/repos")]
    [Authorize(Roles = ShieldRoles.Admin)]
    public async Task<ActionResult<GitHubRepoListResponse>> GitHubRepos(
        [FromQuery] string? affiliation,
        [FromQuery] int perPage,
        CancellationToken ct
    )
    {
        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(OAuthProvider.Github, ct);
        if (token is null)
            return BadRequest(new { error = "github_not_connected" });

        if (
            !_registry.TryResolve(OAuthProvider.Github, out IOAuthProvider adapter)
            || adapter is not GitHubProvider githubAdapter
        )
        {
            return StatusCode(500, new { error = "github_adapter_unavailable" });
        }

        string normalisedAffiliation = string.IsNullOrWhiteSpace(affiliation)
            ? "owner,collaborator,organization_member"
            : affiliation!;
        string userKey = User?.Identity?.Name ?? "anon";
        string cacheKey = $"github-repos::{userKey}::{normalisedAffiliation}";

        if (
            _memoryCache.TryGetValue(cacheKey, out GitHubRepoListResponse? cached)
            && cached is not null
        )
            return Ok(cached);

        try
        {
            IReadOnlyList<GitHubRepoEntry> repos = await githubAdapter.ListReposAsync(
                token.AccessToken,
                normalisedAffiliation,
                perPage <= 0 ? 100 : Math.Min(perPage, 100),
                maxRepos: 1000,
                ct
            );
            GitHubRepoListResponse response = new(repos, repos.Count);
            _memoryCache.Set(cacheKey, response, GithubReposCacheTtl);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub repo list failed");
            return StatusCode(502, new { error = "github_api_failed" });
        }
    }

    private static bool TryParseProvider(string raw, out OAuthProvider provider)
    {
        // Accept lowercase ("github") or canonical case ("Github").
        if (Enum.TryParse(raw, ignoreCase: true, out provider))
            return true;
        provider = default;
        return false;
    }

    private static OAuthIntent ParseIntent(string? raw) =>
        string.Equals(raw, "signin", StringComparison.OrdinalIgnoreCase)
            ? OAuthIntent.Signin
            : OAuthIntent.Connect;

    // Only accept local same-origin redirects so an open-redirect can't piggyback on /api/oauth/start.
    private static string SanitizeRedirect(string? raw, OAuthIntent intent)
    {
        string fallback = intent == OAuthIntent.Signin ? "/" : "/settings";
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal))
            return fallback;
        return raw;
    }

    private bool TryBuildClientConfig(
        AppSettingsSnapshot snapshot,
        OAuthProvider provider,
        out OAuthClientConfig? config,
        out string? error
    )
    {
        OAuthClientSettings client = provider switch
        {
            OAuthProvider.Github => snapshot.GithubOAuth,
            OAuthProvider.Slack => snapshot.SlackOAuth,
            OAuthProvider.Google => snapshot.GoogleOAuth,
            _ => new OAuthClientSettings(null, null, null),
        };
        if (string.IsNullOrEmpty(client.ClientId) || string.IsNullOrEmpty(client.ClientSecret))
        {
            config = null;
            error = $"{provider} client id/secret not configured";
            return false;
        }
        string redirectBase = (
            snapshot.OAuthRedirectBase ?? $"{Request.Scheme}://{Request.Host.Value}"
        ).TrimEnd('/');
        config = new OAuthClientConfig(
            client.ClientId!,
            client.ClientSecret!,
            $"{redirectBase}/api/oauth/{provider.ToString().ToLowerInvariant()}/callback"
        );
        error = null;
        return true;
    }

    private static string ResolveScopes(
        AppSettingsSnapshot snapshot,
        OAuthProvider provider,
        IOAuthProvider adapter,
        OAuthIntent intent
    )
    {
        // Signin always uses the adapter's signin scopes — the operator's configured scope list
        // is for the connect (integration install) flow and may include bot-only scopes Slack
        // wouldn't accept on the OIDC endpoint.
        if (intent == OAuthIntent.Signin)
            return adapter.SigninDefaultScopes;

        OAuthClientSettings client = provider switch
        {
            OAuthProvider.Github => snapshot.GithubOAuth,
            OAuthProvider.Slack => snapshot.SlackOAuth,
            OAuthProvider.Google => snapshot.GoogleOAuth,
            _ => new OAuthClientSettings(null, null, null),
        };
        return string.IsNullOrWhiteSpace(client.Scopes) ? adapter.DefaultScopes : client.Scopes!;
    }

    private IActionResult RedirectToReturn(OAuthStateEntry? entry, OAuthIntent intent, string error)
    {
        string esc = Uri.EscapeDataString(error);
        if (intent == OAuthIntent.Signin)
        {
            string baseUrl = SanitizeRedirect(entry?.ReturnUrl, OAuthIntent.Signin);
            string separator = baseUrl.Contains('?') ? "&" : "?";
            return Redirect($"{baseUrl}{separator}oauth_signin_rejected={esc}");
        }
        return Redirect($"/settings?oauth_error={esc}");
    }

    private static string GenerateUrlToken(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string GenerateRandomPassword()
    {
        Span<byte> buffer = stackalloc byte[24];
        RandomNumberGenerator.Fill(buffer);
        // Identity password policy needs digit + non-uppercase; "Aa1" prefix guarantees both.
        return "Aa1!"
            + Convert.ToBase64String(buffer).Replace('+', 'A').Replace('/', 'a').Replace('=', '0');
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static class Pkce
    {
        public static string S256Challenge(string verifier)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
            return Base64UrlEncode(hash);
        }
    }
}
