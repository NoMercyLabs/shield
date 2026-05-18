using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Auth;

// Idempotent startup seeder: ensures Admin, Maintainer, and Viewer roles exist.
// First-run account creation is handled by POST /api/auth/setup.
public static class IdentitySeeder
{
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
            string roleName in new[]
            {
                ShieldRoles.Admin,
                ShieldRoles.Maintainer,
                ShieldRoles.Viewer,
            }
        )
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                IdentityResult result = await roleManager.CreateAsync(new(roleName));
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed role '{roleName}': {string.Join(", ", result.Errors.Select(error => error.Description))}"
                    );
            }
        }
    }
}
