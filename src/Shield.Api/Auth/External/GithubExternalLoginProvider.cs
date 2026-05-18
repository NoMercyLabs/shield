namespace Shield.Api.Auth.External;

// GitHub adapter for the signin-flow.
//
// Why: GitHub's OAuth App and Device Flow share one client_id and one scope set, so the
// published Shield OAuth App (Ov23libI6hv5NBmkaxjV) doubles as the signin authenticator
// AND the source-scanning bearer. Using the device flow here keeps self-hosted operators
// out of the OAuth-App registration song-and-dance — they just click "Sign in with
// GitHub", paste the user_code on github.com, and Shield reads their profile to look up
// (LoginProvider="github", ProviderKey=numeric-id) in AspNetUserLogins.
public sealed class GithubExternalLoginProvider : IExternalLoginProvider
{
    public const string ProviderKey = "github";

    // Same client id as OAuthController's connect-flow baked-in default. Override via
    // Shield:OAuth:GitHub:DefaultClientId for forks; per-instance Shield:OAuth:GitHub:ClientId
    // (in settings) wins for operators who registered their own App.
    private const string BakedInClientId = "Ov23libI6hv5NBmkaxjV";

    // Signin needs the user profile only — read:user covers /user. We deliberately do NOT
    // request `repo`/`public_repo` here because this is a signin authenticator, not the
    // source-scanning connect flow. Operators who want both run the connect flow separately
    // from /settings, which uses its own scope set.
    private const string SigninScopes = "read:user";

    private readonly IGitHubDeviceFlowClient _deviceFlow;
    private readonly IExternalLoginFlowStore _flowStore;
    private readonly IAppSettingsService _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GithubExternalLoginProvider> _logger;

    public GithubExternalLoginProvider(
        IGitHubDeviceFlowClient deviceFlow,
        IExternalLoginFlowStore flowStore,
        IAppSettingsService settings,
        IConfiguration configuration,
        ILogger<GithubExternalLoginProvider> logger
    )
    {
        _deviceFlow = deviceFlow;
        _flowStore = flowStore;
        _settings = settings;
        _configuration = configuration;
        _logger = logger;
    }

    public string Key => ProviderKey;
    public string DisplayName => "GitHub";
    public string IconKey => "github";

    public async Task<ExternalLoginStartResult> StartSigninAsync(
        string returnPath,
        CancellationToken ct
    )
    {
        string clientId = await ResolveClientIdAsync(ct);
        GitHubDeviceCodeResponse code = await _deviceFlow.RequestDeviceCodeAsync(
            clientId,
            SigninScopes,
            ct
        );

        // OAuth Apps return null for verification_uri_complete; synthesize a pre-filled URL
        // so the user lands on github.com with the code already in the form.
        string verificationUriComplete = !string.IsNullOrEmpty(code.VerificationUriComplete)
            ? code.VerificationUriComplete!
            : $"{code.VerificationUri}?user_code={Uri.EscapeDataString(code.UserCode)}";

        string flowId = _flowStore.Issue(
            new(
                ProviderKey,
                code.DeviceCode,
                clientId,
                SigninScopes,
                returnPath,
                DateTime.UtcNow.AddSeconds(Math.Max(code.ExpiresIn, 60))
            )
        );

        return new(
            flowId,
            code.UserCode,
            code.VerificationUri,
            verificationUriComplete,
            Math.Max(code.Interval, 1),
            Math.Max(code.ExpiresIn, 60)
        );
    }

    public async Task<ExternalLoginPollResult> PollSigninAsync(string flowId, CancellationToken ct)
    {
        ExternalLoginFlowEntry? entry = _flowStore.Find(flowId);
        if (entry is null)
            return new(ExternalLoginPollStatus.Expired);

        GitHubDeviceTokenResponse tokenResponse;
        try
        {
            tokenResponse = await _deviceFlow.PollAccessTokenAsync(
                entry.ClientId,
                entry.DeviceCode,
                ct
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub external-login poll transport failure");
            return new(ExternalLoginPollStatus.Error);
        }

        if (!string.IsNullOrEmpty(tokenResponse.Error))
        {
            switch (tokenResponse.Error)
            {
                case "authorization_pending":
                    return new(ExternalLoginPollStatus.Pending);
                case "slow_down":
                    return new(ExternalLoginPollStatus.SlowDown);
                case "expired_token":
                    _flowStore.Remove(flowId);
                    return new(ExternalLoginPollStatus.Expired);
                case "access_denied":
                    _flowStore.Remove(flowId);
                    return new(ExternalLoginPollStatus.Denied);
                default:
                    _logger.LogWarning(
                        "GitHub external-login unexpected error {Error}: {Description}",
                        tokenResponse.Error,
                        tokenResponse.ErrorDescription
                    );
                    return new(ExternalLoginPollStatus.Error);
            }
        }

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            return new(ExternalLoginPollStatus.Error);

        GitHubUserProfile? profile = await _deviceFlow.FetchUserProfileAsync(
            tokenResponse.AccessToken,
            ct
        );
        if (
            profile is null
            || string.IsNullOrEmpty(profile.Login)
            || string.IsNullOrEmpty(profile.Id)
        )
            return new(ExternalLoginPollStatus.Error);

        _flowStore.Remove(flowId);

        string scope = tokenResponse.Scope ?? SigninScopes;
        string[] scopes = scope.Split(
            [',', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        ExternalIdentity identity = new(
            ProviderKey,
            profile.Id,
            profile.Login,
            Email: null,
            AvatarUrl: profile.AvatarUrl,
            AccessToken: tokenResponse.AccessToken!,
            Scopes: scopes
        );
        return new(ExternalLoginPollStatus.Ok, identity);
    }

    private async Task<string> ResolveClientIdAsync(CancellationToken ct)
    {
        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        if (!string.IsNullOrEmpty(snapshot.GithubOAuth.ClientId))
            return snapshot.GithubOAuth.ClientId!;
        string? defaultClientId =
            _configuration["Shield:OAuth:GitHub:DefaultClientId"]
            ?? _configuration["Shield:OAuth:Github:DefaultClientId"];
        return string.IsNullOrEmpty(defaultClientId) ? BakedInClientId : defaultClientId;
    }
}
