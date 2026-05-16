using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Services;

// Reads/writes the two webhook secrets via the AppSettings table using the same
// "shield.settings" DataProtector purpose that AppSettingsService uses. Lives outside
// AppSettingsService because SettingsController writes the AppSettingsSnapshot record
// wholesale; adding two more secrets to that snapshot would force a SettingsController
// edit, and another agent owns that file.
public interface IWebhookSecretProvider
{
    Task<string?> GetGithubSecretAsync(CancellationToken ct = default);
    Task<string?> GetDependabotSecretAsync(CancellationToken ct = default);
    Task SaveAsync(string? githubSecret, string? dependabotSecret, CancellationToken ct = default);
}

public static class WebhookSecretKeys
{
    public const string GithubSecret = "webhooks.github.secret";
    public const string DependabotSecret = "webhooks.dependabot.secret";
}

public sealed class WebhookSecretProvider : IWebhookSecretProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;

    public WebhookSecretProvider(
        IServiceScopeFactory scopeFactory,
        IDataProtectionProvider protectionProvider
    )
    {
        _scopeFactory = scopeFactory;
        _protector = protectionProvider.CreateProtector("shield.settings");
    }

    public Task<string?> GetGithubSecretAsync(CancellationToken ct = default) =>
        ReadAsync(WebhookSecretKeys.GithubSecret, ct);

    public Task<string?> GetDependabotSecretAsync(CancellationToken ct = default) =>
        ReadAsync(WebhookSecretKeys.DependabotSecret, ct);

    public async Task SaveAsync(
        string? githubSecret,
        string? dependabotSecret,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        DateTime now = DateTime.UtcNow;

        await WriteAsync(db, WebhookSecretKeys.GithubSecret, githubSecret, now, ct);
        await WriteAsync(db, WebhookSecretKeys.DependabotSecret, dependabotSecret, now, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        AppSetting? row = await db.AppSettings.FirstOrDefaultAsync(entry => entry.Key == key, ct);
        if (row is null || string.IsNullOrEmpty(row.ValueEncrypted))
            return null;
        try
        {
            string decrypted = _protector.Unprotect(row.ValueEncrypted);
            return string.IsNullOrEmpty(decrypted) ? null : decrypted;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteAsync(
        ShieldDbContext db,
        string key,
        string? value,
        DateTime now,
        CancellationToken ct
    )
    {
        string encrypted = _protector.Protect(value ?? string.Empty);
        AppSetting? row = await db.AppSettings.FirstOrDefaultAsync(entry => entry.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(
                new AppSetting
                {
                    Key = key,
                    ValueEncrypted = encrypted,
                    UpdatedAt = now,
                }
            );
        }
        else
        {
            row.ValueEncrypted = encrypted;
            row.UpdatedAt = now;
        }
    }
}
