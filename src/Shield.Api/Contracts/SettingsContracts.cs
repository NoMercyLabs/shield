namespace Shield.Api.Contracts;

public sealed record SettingsResponse(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecretMasked,
    Severity AlertSeverityFloor,
    int RetentionDays,
    OAuthProviderConfigResponse Github,
    OAuthProviderConfigResponse Slack,
    OAuthProviderConfigResponse Google,
    string? OAuthRedirectBase = null
);

// Configured is true iff both ClientId and ClientSecret are set; ClientSecretMasked is "****<last4>" when present.
public sealed record OAuthProviderConfigResponse(
    string? ClientId,
    string? ClientSecretMasked,
    string? Scopes,
    bool Configured
);

public sealed record UpdateSettingsRequest(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecret,
    Severity AlertSeverityFloor,
    int RetentionDays,
    OAuthProviderConfigPatch? Github = null,
    OAuthProviderConfigPatch? Slack = null,
    OAuthProviderConfigPatch? Google = null,
    // Override for the base URL Shield uses when constructing redirect_uri for OAuth code
    // flows. Falls back to `{Request.Scheme}://{Request.Host}` when null — set explicitly
    // when running behind a proxy/tunnel where the auto-detected scheme is wrong.
    string? OAuthRedirectBase = null
);

// ClientSecret semantics: null = leave existing, "" = clear, non-empty = overwrite.
public sealed record OAuthProviderConfigPatch(
    string? ClientId,
    string? ClientSecret,
    string? Scopes
);

public sealed record UpdateSettingsResponse(
    SettingsResponse Settings,
    bool RequiresRestart,
    IReadOnlyList<string> RestartKeys
);

public sealed record TestOidcRequest(string Issuer, string ClientId, string? ClientSecret);

public sealed record TestOidcResponse(bool Ok, string? Error);

public sealed record RuntimeInfoResponse(
    string Version,
    string Environment,
    string ContentRoot,
    string WebRoot
);
