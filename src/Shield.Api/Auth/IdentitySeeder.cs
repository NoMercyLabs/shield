using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shield.Core.Options;
using Shield.Data.Identity;

namespace Shield.Api.Auth;

// Idempotent startup seeder: ensures Admin/Viewer roles exist, and in single-user
// mode provisions the synthetic single-user@shield.local Admin with a one-shot
// random password (logged to console exactly once, never persisted unhashed).
public static class IdentitySeeder
{
    public const string SingleUserEmail = "single-user@shield.local";
    public const string SingleUserName = "single-user";

    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        RoleManager<ShieldRole> roleManager = scope.ServiceProvider.GetRequiredService<
            RoleManager<ShieldRole>
        >();

        foreach (
            string roleName in new[] { ShieldRoles.Admin, ShieldRoles.Maintainer, ShieldRoles.Viewer }
        )
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                IdentityResult result = await roleManager.CreateAsync(new ShieldRole(roleName));
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join(", ", result.Errors.Select(error => error.Description))}"
                    );
            }
        }

        IOptions<ShieldOptions> shieldOptions = scope.ServiceProvider.GetRequiredService<
            IOptions<ShieldOptions>
        >();
        if (!shieldOptions.Value.SingleUser)
            return;

        UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
            UserManager<ShieldUser>
        >();
        ILogger logger = scope
            .ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Shield.Api.Auth.IdentitySeeder");

        ShieldUser? existing = await userManager.FindByNameAsync(SingleUserName);
        if (existing is not null)
            return;

        string generatedPassword = GenerateRandomPassword();
        ShieldUser user = new()
        {
            Id = Guid.Parse(SingleUserMiddleware.SyntheticUserId),
            UserName = SingleUserName,
            Email = SingleUserEmail,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult create = await userManager.CreateAsync(user, generatedPassword);
        if (!create.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed single-user account: {string.Join(", ", create.Errors.Select(error => error.Description))}"
            );

        IdentityResult role = await userManager.AddToRoleAsync(user, ShieldRoles.Admin);
        if (!role.Succeeded)
            throw new InvalidOperationException(
                $"Failed to grant Admin role to single-user: {string.Join(", ", role.Errors.Select(error => error.Description))}"
            );

        logger.LogWarning(
            "Seeded single-user account {Username} with one-shot password: {Password}. Save it now — it is not stored.",
            SingleUserName,
            generatedPassword
        );
    }

    private static string GenerateRandomPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'a').Replace('=', '0');
    }
}
