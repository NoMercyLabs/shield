using Shield.Scanners;

namespace Shield.Api.Services.Security;

// Bridges IAppSettingsService.Current.DetectedRemoteHosts → scanner-side policy.
public sealed class AppSettingsRemoteHostPolicy : IDetectedRemoteHostPolicy
{
    private readonly IAppSettingsService _settings;

    public AppSettingsRemoteHostPolicy(IAppSettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyCollection<string> ActionableHosts => _settings.Current.DetectedRemoteHosts;
}
