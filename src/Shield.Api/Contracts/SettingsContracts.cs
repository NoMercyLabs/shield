using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record SettingsResponse(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecretMasked,
    Severity AlertSeverityFloor,
    int RetentionDays
);

public sealed record UpdateSettingsRequest(
    bool SingleUserMode,
    bool OpenApiEnabled,
    bool OidcEnabled,
    string? OidcIssuer,
    string? OidcClientId,
    string? OidcClientSecret,
    Severity AlertSeverityFloor,
    int RetentionDays
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
