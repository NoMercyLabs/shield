namespace Shield.Api.Services.AppSettings;

public static class AppSettingKeys
{
    public const string SingleUserMode = "singleUserMode";
    public const string OpenApiEnabled = "openApiEnabled";
    public const string OidcEnabled = "oidcEnabled";
    public const string OidcIssuer = "oidcIssuer";
    public const string OidcClientId = "oidcClientId";
    public const string OidcClientSecret = "oidcClientSecret";
    public const string AlertSeverityFloor = "alertSeverityFloor";
    public const string RetentionDays = "retentionDays";
    public const string RegistrationOpen = "registrationOpen";

    public const string OnboardingDismissed = "onboarding.dismissed";

    // Feed cadence overrides — KEV/EPSS feed syncs read these directly (24h default each).
    public const string KevCadenceHours = "feeds.kev.cadence_hours";
    public const string EpssCadenceHours = "feeds.epss.cadence_hours";

    public const string OAuthRedirectBase = "oauth.redirectBase";
    public const string GithubOAuthClientId = "oauth.github.clientId";
    public const string GithubOAuthClientSecret = "oauth.github.clientSecret";
    public const string GithubOAuthScopes = "oauth.github.scopes";
    public const string SlackOAuthClientId = "oauth.slack.clientId";
    public const string SlackOAuthClientSecret = "oauth.slack.clientSecret";
    public const string SlackOAuthScopes = "oauth.slack.scopes";
    public const string GoogleOAuthClientId = "oauth.google.clientId";
    public const string GoogleOAuthClientSecret = "oauth.google.clientSecret";
    public const string GoogleOAuthScopes = "oauth.google.scopes";

    // 2FA enforcement gate — when true, TwoFactorEnforcementMiddleware 403s any non-2FA
    // user trying to reach the API. Keyed under `auth.` so future auth settings group cleanly.
    public const string AuthRequire2Fa = "auth.require_2fa";

    // Web Push VAPID identity — generated on first push attempt if absent. Both keys are
    // base64url-encoded EC P-256 raw values (private = 32 bytes, public = 65 bytes uncompressed
    // point). PublicKey is exposed anonymously so any signed-in browser can subscribe;
    // PrivateKey stays in AppSettings (encrypted at rest by DataProtection).
    public const string PushVapidPublicKey = "push.vapid.publicKey";
    public const string PushVapidPrivateKey = "push.vapid.privateKey";
    public const string PushVapidSubject = "push.vapid.subject";
}
