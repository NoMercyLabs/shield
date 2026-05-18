namespace Shield.Api.Services.Auth;

// Centralizes the policy read so the middleware + admin UI + auth flow all agree.
public interface ITwoFactorEnforcement
{
    Task<bool> IsRequiredAsync(CancellationToken ct = default);
    Task SetRequiredAsync(bool required, Guid? updatedBy, CancellationToken ct = default);
}
