namespace Shield.Api.Services.Auth;

public sealed class TwoFactorEnforcement : ITwoFactorEnforcement
{
    private readonly IAppSettingsService _settings;

    public TwoFactorEnforcement(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public Task<bool> IsRequiredAsync(CancellationToken ct = default) =>
        _settings.GetBoolAsync(AppSettingKeys.AuthRequire2Fa, fallback: false, ct);

    public Task SetRequiredAsync(bool required, Guid? updatedBy, CancellationToken ct = default) =>
        _settings.SetBoolAsync(AppSettingKeys.AuthRequire2Fa, required, updatedBy, ct);
}
