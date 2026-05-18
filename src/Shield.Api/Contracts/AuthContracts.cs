namespace Shield.Api.Contracts;

public sealed record LoginRequest(
    string Username,
    string Password,
    string? TwoFactorCode = null,
    bool RememberMe = false
);

public sealed record LoginResponse(
    string? UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    bool RequiresTwoFactor = false,
    string? Error = null,
    bool Succeeded = false
);

public sealed record RegisterRequest(string Username, string Password, string? Email = null);

public sealed record RegisterResponse(string UserId, string Username, IReadOnlyList<string> Roles);

// Issued by POST /api/auth/token for headless clients. The JWT embeds the user's current
// SecurityStamp so it's revoked automatically on password change / 2FA toggle / lockout.
public sealed record TokenResponse(
    string? UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    string Token
);

public sealed record MeResponse(
    string? UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    bool SingleUserMode,
    // Decoration pulled from the user's connected code-host identity (e.g. GitHub) — used by
    // the SPA to render avatar / display-name in the topbar without an extra round-trip.
    // Null when the user hasn't connected any external provider.
    string? DisplayName = null,
    string? AvatarUrl = null,
    string? ProfileUrl = null,
    string? ProviderLogin = null,
    string? ProviderKey = null,
    // Impersonation surface — non-null when this response is being returned through an active
    // "Admin viewing as X" override. SPA renders the banner + Exit button off these fields.
    string? ImpersonatedBy = null,
    string? ImpersonatorLogin = null
);

public sealed record ImpersonationStartRequest(string UserId);

public sealed record ImpersonationStartResponse(Guid UserId, string Username);

public sealed record TwoFactorEnrollResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorVerifyRequest(string Code);

public sealed record TwoFactorVerifyResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RegistrationAllowedResponse(bool Allowed, string? Reason);

// Anonymous discovery list for the login/register views — only configured providers appear.
public sealed record AuthProviderInfo(string Provider, string DisplayName, string IconUrl);

public sealed record AuthProvidersResponse(IReadOnlyList<AuthProviderInfo> Providers);
