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
    string? Error = null
);

public sealed record RegisterRequest(string Username, string Password, string? Email = null);

public sealed record RegisterResponse(string UserId, string Username, IReadOnlyList<string> Roles);

public sealed record MeResponse(
    string? UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    bool SingleUserMode
);

public sealed record TwoFactorEnrollResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorVerifyRequest(string Code);

public sealed record TwoFactorVerifyResponse(IReadOnlyList<string> RecoveryCodes);
