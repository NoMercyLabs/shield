using Microsoft.AspNetCore.DataProtection;
using Shield.Api.Services;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class OnboardingController : ControllerBase
{
    private readonly ShieldDbContext _db;
    private readonly IOAuthTokenStore _tokens;
    private readonly IAppSettingsService _settings;
    private readonly IDataProtector _protector;

    public OnboardingController(
        ShieldDbContext db,
        IOAuthTokenStore tokens,
        IAppSettingsService settings,
        IDataProtectionProvider protectionProvider
    )
    {
        _db = db;
        _tokens = tokens;
        _settings = settings;
        // Same purpose as AppSettingsService so the dismissed flag stays consistent across
        // restarts and survives the encrypted-row round-trip.
        _protector = protectionProvider.CreateProtector("shield.settings");
    }

    [HttpGet("status")]
    public async Task<ActionResult<OnboardingStatusResponse>> GetStatus(CancellationToken ct)
    {
        int sourceCount = await _db.Sources.CountAsync(ct);
        int channelCount = await _db.AlertChannels.CountAsync(ct);

        OAuthTokenSnapshot? githubToken = await _tokens.GetAsync(OAuthProvider.Github, ct);
        bool githubConnected = githubToken is not null;

        AppSettingsSnapshot snapshot = await _settings.GetAsync(ct);
        bool anyOauthConfigured =
            !string.IsNullOrEmpty(snapshot.GithubOAuth.ClientId)
            || !string.IsNullOrEmpty(snapshot.SlackOAuth.ClientId)
            || !string.IsNullOrEmpty(snapshot.GoogleOAuth.ClientId);

        bool dismissed = await ReadDismissedAsync(ct);
        bool completed = dismissed || (sourceCount > 0 && channelCount > 0);

        return Ok(
            new OnboardingStatusResponse(
                completed,
                sourceCount,
                channelCount,
                githubConnected,
                anyOauthConfigured
            )
        );
    }

    [HttpPost("dismiss")]
    public async Task<ActionResult<OnboardingStatusResponse>> Dismiss(CancellationToken ct)
    {
        await WriteDismissedAsync(true, ct);
        await _settings.ReloadAsync(ct);
        return await GetStatus(ct);
    }

    private async Task<bool> ReadDismissedAsync(CancellationToken ct)
    {
        AppSetting? row = await _db
            .AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Key == AppSettingKeys.OnboardingDismissed, ct);
        if (row is null)
            return false;
        try
        {
            string value = _protector.Unprotect(row.ValueEncrypted);
            return bool.TryParse(value, out bool parsed) && parsed;
        }
        catch
        {
            return false;
        }
    }

    private async Task WriteDismissedAsync(bool dismissed, CancellationToken ct)
    {
        string encrypted = _protector.Protect(dismissed ? "true" : "false");
        AppSetting? existing = await _db.AppSettings.FirstOrDefaultAsync(
            setting => setting.Key == AppSettingKeys.OnboardingDismissed,
            ct
        );
        DateTime now = DateTime.UtcNow;
        if (existing is null)
        {
            _db.AppSettings.Add(
                new()
                {
                    Key = AppSettingKeys.OnboardingDismissed,
                    ValueEncrypted = encrypted,
                    UpdatedAt = now,
                }
            );
        }
        else
        {
            existing.ValueEncrypted = encrypted;
            existing.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }
}
