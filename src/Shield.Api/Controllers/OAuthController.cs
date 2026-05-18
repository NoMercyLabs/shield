using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Shield.Api.Auth.OAuthProviders;

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
    private readonly IGitHubDeviceFlowClient _deviceFlowClient;
    private readonly IGitHubDeviceFlowStore _deviceFlowStore;
    private readonly ISessionTracker _sessionTracker;
    private readonly ISessionCookieIssuer _sessionCookieIssuer;
    private readonly IAuditLogger _audit;
    private readonly ISessionAuditor _sessionAuditor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthController> _logger;

    private static readonly TimeSpan GithubReposCacheTtl = TimeSpan.FromMinutes(5);

    // GitHub's published Shield OAuth App — public client_id, baked in so self-hosted users
    // never have to register their own OAuth App for the device flow. Overridable via
    // Shield:OAuth:GitHub:DefaultClientId in config (and per-user via the existing GithubOAuth
    // ClientId setting, which takes precedence).
    private const string BakedInGithubDeviceFlowClientId = "Ov23libI6hv5NBmkaxjV";

    // read:org is required so post-signin we can mirror the user's GitHub org memberships into
    // Shield's per-source ACL (see GithubAccessResolver). Users with tokens minted under the
    // old narrower scope need to re-sign-in once before the new scope kicks in.
    private const string GithubDeviceFlowScopes = "read:user user:email public_repo read:org";

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
        IGitHubDeviceFlowClient deviceFlowClient,
        IGitHubDeviceFlowStore deviceFlowStore,
        ISessionTracker sessionTracker,
        ISessionCookieIssuer sessionCookieIssuer,
        IAuditLogger audit,
        ISessionAuditor sessionAuditor,
        IConfiguration configuration,
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
        _deviceFlowClient = deviceFlowClient;
        _deviceFlowStore = deviceFlowStore;
        _sessionTracker = sessionTracker;
        _sessionCookieIssuer = sessionCookieIssuer;
        _audit = audit;
        _sessionAuditor = sessionAuditor;
        _configuration = configuration;
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
            new(
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
                entry.ReturnUrl,
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

        // Mirror the binding into AspNetUserLogins so UserManager.FindByLoginAsync /
        // GetLoginsAsync see it. Accept-invite + future password-less flows read from here.
        // No-op if the same (provider, subject) already exists for the user.
        IList<UserLoginInfo> existingExternalLogins = await _userManager.GetLoginsAsync(user);
        // Lowercased to match the external-login pipeline's "github" convention so
        // FindByLoginAsync from either signin path hits the same AspNetUserLogins row.
        string providerKey = adapter.Provider.ToString().ToLowerInvariant();
        if (
            !existingExternalLogins.Any(login =>
                string.Equals(login.LoginProvider, providerKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(login.ProviderKey, result.Subject, StringComparison.Ordinal)
            )
        )
        {
            await _userManager.AddLoginAsync(user, new(providerKey, result.Subject, result.Login));
        }

        // Mirror GitHub org/repo permissions into Shield's ACL on the way in so the user's
        // first authenticated render of /sources already shows their team's repos. Best-effort:
        // a failure here MUST NOT kill signin (the manual ACL layer still applies).
        if (adapter.Provider == OAuthProvider.Github)
        {
            try
            {
                IGithubAccessResolver githubAccess =
                    HttpContext.RequestServices.GetRequiredService<IGithubAccessResolver>();
                _ = await githubAccess.RefreshAsync(user.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "GitHub access mirror failed during signin for user {UserId}; manual ACL still applies",
                    user.Id
                );
            }
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        UserSession session = await _sessionCookieIssuer.IssueAsync(HttpContext, user.Id, ct);

        SigninMethod oauthMethod = adapter.Provider switch
        {
            OAuthProvider.Github => SigninMethod.GithubOAuth,
            OAuthProvider.Google => SigninMethod.GoogleOAuth,
            OAuthProvider.Slack => SigninMethod.SlackOAuth,
            _ => SigninMethod.GithubOAuth,
        };
        await _sessionAuditor.RecordSigninAsync(user, session, oauthMethod, ct);

        return Redirect(string.IsNullOrEmpty(entry.ReturnUrl) ? "/" : entry.ReturnUrl);
    }

    private async Task<(ShieldUser? user, string? reject)> TryProvisionUserAsync(
        OAuthProvider provider,
        OAuthSigninResult signin,
        string? returnUrl,
        CancellationToken ct
    )
    {
        // First real user wins the Admin role — synthetic single-user is excluded from the count.
        bool isFirstUser = !await _userManager.Users.AnyAsync(
            candidate => candidate.UserName != IdentitySeeder.SingleUserName,
            ct
        );

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);

        // Pending-invite path: when the signin originated from /accept-invite?token=X, look up
        // the invite. A valid + unaccepted + not-revoked invite whose pre-bound subject matches
        // (or no pre-binding) is a stronger grant than RegistrationOpen — proceed regardless.
        bool hasValidInvite = false;
        if (!isFirstUser && !string.IsNullOrEmpty(returnUrl))
        {
            string? inviteToken = TryExtractInviteToken(returnUrl);
            if (!string.IsNullOrEmpty(inviteToken))
            {
                ShieldDbContext db =
                    HttpContext.RequestServices.GetRequiredService<ShieldDbContext>();
                Invite? invite = await db.Invites.FirstOrDefaultAsync(
                    item => item.Token == inviteToken,
                    ct
                );
                if (
                    invite is not null
                    && invite.AcceptedAt is null
                    && invite.RevokedAt is null
                    && invite.ExpiresAt > DateTime.UtcNow
                )
                {
                    bool subjectOk =
                        string.IsNullOrEmpty(invite.PreBoundSubjectId)
                        || string.Equals(
                            invite.PreBoundSubjectId,
                            signin.Subject,
                            StringComparison.Ordinal
                        );
                    if (subjectOk)
                        hasValidInvite = true;
                }
            }
        }

        if (!isFirstUser && !snapshot.RegistrationOpen && !hasValidInvite)
            return (null, "oauth_signin_rejected");

        string role = isFirstUser ? ShieldRoles.Admin : ShieldRoles.Viewer;
        if (!await _roleManager.RoleExistsAsync(role))
            await _roleManager.CreateAsync(new(role));

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

    // ReturnUrl is sanitised earlier so it's a relative path. Pull `?token=…` out so we can
    // resolve the matching Invite row for the OAuth provision policy. Returns null when the
    // url isn't an accept-invite path or has no token param.
    private static string? TryExtractInviteToken(string returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
            return null;
        int qIndex = returnUrl.IndexOf('?');
        if (qIndex < 0)
            return null;
        string path = returnUrl.Substring(0, qIndex);
        if (
            !path.StartsWith("/accept-invite", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("accept-invite", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        string query = returnUrl.Substring(qIndex + 1);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
                continue;
            string name = pair.Substring(0, eq);
            if (string.Equals(name, "token", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
        return null;
    }

    [HttpPost("{provider}/disconnect")]
    [Authorize(Policy = ShieldPolicies.Admin)]
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

        bool deviceFlowAvailable = IsDeviceFlowAvailable(parsedProvider);

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
                    UpdatedAt: null,
                    DeviceFlowAvailable: deviceFlowAvailable
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
                UpdatedAt: null,
                DeviceFlowAvailable: deviceFlowAvailable
            )
        );
    }

    [HttpPost("github/device/start")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<GitHubDeviceStartResponse>> StartGithubDeviceFlow(
        CancellationToken ct
    )
    {
        if (!IsDeviceFlowEnabled())
            return BadRequest(new { error = "device_flow_disabled" });

        string? clientId = await ResolveGithubDeviceFlowClientIdAsync(ct);
        if (string.IsNullOrEmpty(clientId))
            return BadRequest(new { error = "github_client_id_unavailable" });

        try
        {
            GitHubDeviceCodeResponse code = await _deviceFlowClient.RequestDeviceCodeAsync(
                clientId!,
                GithubDeviceFlowScopes,
                ct
            );
            string flowId = _deviceFlowStore.Issue(
                new(
                    code.DeviceCode,
                    clientId!,
                    GithubDeviceFlowScopes,
                    DateTime.UtcNow.AddSeconds(Math.Max(code.ExpiresIn, 60))
                )
            );
            // GitHub Apps return verification_uri_complete with the code pre-filled; OAuth Apps
            // (which Shield's published default is) return null. The verification page accepts
            // ?user_code= as a query param regardless, so synthesize one when GitHub didn't.
            string verificationUriComplete = !string.IsNullOrEmpty(code.VerificationUriComplete)
                ? code.VerificationUriComplete!
                : $"{code.VerificationUri}?user_code={Uri.EscapeDataString(code.UserCode)}";

            return Ok(
                new GitHubDeviceStartResponse(
                    flowId,
                    code.UserCode,
                    code.VerificationUri,
                    code.ExpiresIn,
                    code.Interval,
                    verificationUriComplete
                )
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub device-flow start failed");
            return StatusCode(502, new { error = "github_device_start_failed" });
        }
    }

    [HttpPost("github/device/poll")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<ActionResult<GitHubDevicePollResponse>> PollGithubDeviceFlow(
        [FromBody] GitHubDevicePollRequest request,
        CancellationToken ct
    )
    {
        if (request is null || string.IsNullOrEmpty(request.FlowId))
            return BadRequest(new { error = "missing_flow_id" });

        GitHubDeviceFlowEntry? entry = _deviceFlowStore.Find(request.FlowId);
        if (entry is null)
            return StatusCode(410, new GitHubDevicePollResponse("expired"));

        GitHubDeviceTokenResponse tokenResponse;
        try
        {
            tokenResponse = await _deviceFlowClient.PollAccessTokenAsync(
                entry.ClientId,
                entry.DeviceCode,
                ct
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub device-flow poll failed");
            return StatusCode(502, new { error = "github_device_poll_failed" });
        }

        _logger.LogInformation(
            "GitHub device-flow poll: flowId={FlowId} clientId={ClientId} githubError={Error} hasAccessToken={HasToken}",
            request.FlowId,
            entry.ClientId,
            tokenResponse.Error ?? "(none)",
            !string.IsNullOrEmpty(tokenResponse.AccessToken)
        );

        // GitHub's documented poll responses: authorization_pending, slow_down, expired_token,
        // access_denied, or a successful access_token payload.
        if (!string.IsNullOrEmpty(tokenResponse.Error))
        {
            switch (tokenResponse.Error)
            {
                case "authorization_pending":
                    return Accepted(new GitHubDevicePollResponse("pending"));
                case "slow_down":
                    return Accepted(new GitHubDevicePollResponse("slow_down"));
                case "expired_token":
                    _deviceFlowStore.Remove(request.FlowId);
                    return StatusCode(410, new GitHubDevicePollResponse("expired"));
                case "access_denied":
                    _deviceFlowStore.Remove(request.FlowId);
                    return StatusCode(403, new GitHubDevicePollResponse("denied"));
                default:
                    _logger.LogWarning(
                        "GitHub device-flow unexpected error {Error}: {Description}",
                        tokenResponse.Error,
                        tokenResponse.ErrorDescription
                    );
                    return BadRequest(new { error = tokenResponse.Error });
            }
        }

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            return BadRequest(new { error = "no_access_token" });

        GitHubUserProfile? profile = await _deviceFlowClient.FetchUserProfileAsync(
            tokenResponse.AccessToken,
            ct
        );
        if (profile is null || string.IsNullOrEmpty(profile.Login))
        {
            return StatusCode(502, new { error = "github_user_probe_failed" });
        }

        // The current Shield user — request is Admin-policy authenticated so this resolves
        // either to a real Identity user, the API-token-backed actor, or the synthetic
        // single-user account, all of which expose a Guid id.
        ShieldUser? currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
            return Unauthorized();

        OAuthTokenSnapshot snapshot = new(
            OAuthProvider.Github,
            tokenResponse.AccessToken!,
            RefreshToken: null,
            ExpiresAt: null,
            Scopes: tokenResponse.Scope ?? GithubDeviceFlowScopes,
            AccountLogin: profile.Login,
            AccountId: profile.Id,
            Extra: null
        );
        await _tokenStore.SaveAsync(snapshot, currentUser.Id, ct);
        _deviceFlowStore.Remove(request.FlowId);

        return Ok(
            new GitHubDevicePollResponse("ok", new(profile.Login, profile.Id, profile.AvatarUrl))
        );
    }

    private bool IsDeviceFlowEnabled() =>
        _configuration.GetValue("Shield:OAuth:GitHub:DeviceFlow:Enabled", true);

    private bool IsDeviceFlowAvailable(OAuthProvider provider)
    {
        if (provider != OAuthProvider.Github)
            return false;
        if (!IsDeviceFlowEnabled())
            return false;
        return !string.IsNullOrEmpty(ResolveGithubDeviceFlowClientIdSync());
    }

    // Sync sibling for the status endpoint — avoids forcing the cheap "is it possible"
    // probe through async overhead. Falls back to the per-user override stored in settings
    // (Current is the cached snapshot AppSettingsService keeps hot for the auth handlers).
    private string? ResolveGithubDeviceFlowClientIdSync()
    {
        string? overrideClientId = _settings.Current.GithubOAuth.ClientId;
        if (!string.IsNullOrEmpty(overrideClientId))
            return overrideClientId;
        string? defaultClientId =
            _configuration["Shield:OAuth:GitHub:DefaultClientId"]
            ?? _configuration["Shield:OAuth:Github:DefaultClientId"];
        return string.IsNullOrEmpty(defaultClientId)
            ? BakedInGithubDeviceFlowClientId
            : defaultClientId;
    }

    private async Task<string?> ResolveGithubDeviceFlowClientIdAsync(CancellationToken ct)
    {
        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        if (!string.IsNullOrEmpty(snapshot.GithubOAuth.ClientId))
            return snapshot.GithubOAuth.ClientId;
        string? defaultClientId =
            _configuration["Shield:OAuth:GitHub:DefaultClientId"]
            ?? _configuration["Shield:OAuth:Github:DefaultClientId"];
        return string.IsNullOrEmpty(defaultClientId)
            ? BakedInGithubDeviceFlowClientId
            : defaultClientId;
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
        request.Headers.Authorization = new("Bearer", token.AccessToken);

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

        List<SlackChannelInfo> channels = [];
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
                    channels.Add(new(id, name, isPrivate));
            }
        }
        return Ok(new SlackChannelsResponse(channels));
    }

    // Polymorphic repo listing — any registered IOAuthProvider that implements
    // ListRepositoriesAsync exposes a normalised RepositorySummary list through this one
    // endpoint. Cached 5 min per (user, provider, affiliation) so reopening the picker
    // modal is cheap. Replaces the previous github-specific /github/repos endpoint;
    // legacy alias kept below for back-compat with older SPA bundles.
    [HttpGet("repos")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public async Task<IActionResult> ListRepositories(
        [FromQuery] string provider,
        [FromQuery] string? affiliation,
        [FromQuery] int perPage,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(provider)
            || !TryParseProvider(provider, out OAuthProvider parsedProvider)
        )
            return BadRequest(new { error = "unknown_provider" });
        if (!_registry.TryResolve(parsedProvider, out IOAuthProvider adapter))
            return BadRequest(new { error = "provider_not_registered" });

        OAuthTokenSnapshot? token = await _tokenStore.GetAsync(parsedProvider, ct);
        if (token is null)
            return BadRequest(new { error = "provider_not_connected" });

        string userKey = User?.Identity?.Name ?? "anon";
        string cacheKey = $"repos::{userKey}::{parsedProvider}::{affiliation ?? ""}";
        if (
            _memoryCache.TryGetValue(cacheKey, out RepositoryListResponse? cached)
            && cached is not null
        )
            return Ok(cached);

        RepositoryListOptions options = new(
            Affiliation: affiliation,
            PerPage: perPage <= 0 ? 100 : Math.Min(perPage, 100),
            MaxRepositories: 1000
        );
        try
        {
            IReadOnlyList<RepositorySummary>? repos = await adapter.ListRepositoriesAsync(
                token.AccessToken,
                options,
                ct
            );
            if (repos is null)
                return BadRequest(new { error = "provider_lacks_repo_listing" });

            RepositoryListResponse response = new(repos, repos.Count);
            _memoryCache.Set(cacheKey, response, GithubReposCacheTtl);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Repo list failed for {Provider}", parsedProvider);
            return StatusCode(502, new { error = "provider_api_failed" });
        }
    }

    // Legacy alias for older SPA bundles — drops once the rebuilt wwwroot is everywhere.
    [HttpGet("github/repos")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    public Task<IActionResult> GitHubRepos(
        [FromQuery] string? affiliation,
        [FromQuery] int perPage,
        CancellationToken ct
    ) => ListRepositories("github", affiliation, perPage, ct);

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
            _ => new(null, null, null),
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
        config = new(
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
            _ => new(null, null, null),
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
