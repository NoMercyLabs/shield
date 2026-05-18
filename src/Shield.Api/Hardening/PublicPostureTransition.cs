using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Data.Identity;

namespace Shield.Api.Hardening;

// One-shot transition: when Shield:Public flips from false→true (detected via a sentinel
// AppSetting row), revoke every OAuth IntegrationToken and bump every user's SecurityStamp
// to invalidate live cookies + JWT principals. Forces a re-auth + re-consent on the new
// public posture so a token cached from the LAN-only era can't surface on the public host.
//
// Idempotent: the sentinel row stores the most recently observed public flag, so flipping
// public off then back on triggers the revoke again (intended — re-private then re-public
// implies the operator wants a fresh slate).
public static class PublicPostureTransition
{
    private const string PublicPostureSentinelKey = "hardening.publicPosture.lastObserved";

    public static async Task RunAsync(IServiceProvider services, ILogger logger)
    {
        bool isPublic;
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        isPublic = configuration.GetValue("Shield:Public", false);

        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        IDataProtectionProvider protectionProvider =
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        IDataProtector protector = protectionProvider.CreateProtector("shield.settings");

        AppSetting? sentinel = await db.AppSettings.FirstOrDefaultAsync(row =>
            row.Key == PublicPostureSentinelKey
        );

        bool previouslyPublic = false;
        if (sentinel is not null)
        {
            try
            {
                previouslyPublic = string.Equals(
                    protector.Unprotect(sentinel.ValueEncrypted),
                    "true",
                    StringComparison.Ordinal
                );
            }
            catch
            {
                // Unreadable sentinel — treat as never-seen so the transition runs once more.
                previouslyPublic = false;
            }
        }

        DateTime now = DateTime.UtcNow;
        string encrypted = protector.Protect(isPublic ? "true" : "false");
        if (sentinel is null)
        {
            db.AppSettings.Add(
                new()
                {
                    Key = PublicPostureSentinelKey,
                    ValueEncrypted = encrypted,
                    UpdatedAt = now,
                }
            );
        }
        else
        {
            sentinel.ValueEncrypted = encrypted;
            sentinel.UpdatedAt = now;
        }
        await db.SaveChangesAsync();

        bool publicFlipOn = isPublic && !previouslyPublic;
        if (!publicFlipOn)
            return;

        int removed = await db.IntegrationTokens.ExecuteDeleteAsync();

        // Bump SecurityStamp on every user — invalidates existing cookies (Identity revalidates
        // the stamp on each request via SignInManager.ValidateSecurityStampAsync) and any JWT
        // principals tied to the stamp via Identity's default token providers.
        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        int stamped = 0;
        List<ShieldUser> users = await userManager.Users.ToListAsync();
        foreach (ShieldUser user in users)
        {
            IdentityResult result = await userManager.UpdateSecurityStampAsync(user);
            if (result.Succeeded)
                stamped++;
        }

        logger.LogWarning(
            "Public posture transitioned to enabled — revoked {TokensRemoved} OAuth integration "
                + "token(s) and bumped SecurityStamp on {UsersStamped} user(s). All existing "
                + "sessions and connected integrations must re-authenticate.",
            removed,
            stamped
        );
    }
}
