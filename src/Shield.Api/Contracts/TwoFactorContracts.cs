namespace Shield.Api.Contracts;

public sealed record TwoFactorEnrollFullResponse(
    string SharedKey,
    string AuthenticatorUri,
    IReadOnlyList<string> RecoveryCodes
);

public sealed record TwoFactorRecoveryRequest(string Username, string RecoveryCode);

public sealed record TwoFactorDisableRequest(string CurrentPassword);

public sealed record TwoFactorStatusResponse(
    bool Enabled,
    bool RequiredByPolicy,
    int RemainingRecoveryCodes
);

public sealed record TwoFactorRequiredProblem(string Code, string EnrollUrl);
