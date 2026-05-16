namespace Shield.Api.Contracts;

public sealed record LoginRequest(
    string Username,
    string Password,
    string? TwoFactorCode = null,
    bool RememberMe = false
);

public sealed record LoginResponse(bool Succeeded, bool RequiresTwoFactor, string? Error);

public sealed record MeResponse(
    string? UserId,
    string? Username,
    IReadOnlyList<string> Roles,
    bool SingleUserMode
);

public sealed record TwoFactorEnrollResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorVerifyRequest(string Code);
