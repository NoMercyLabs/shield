using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shield.Data.Extensions;

public static class MigrationExtensions
{
    public static async Task MigrateShieldAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        ShieldDbContext shieldDb = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        await shieldDb.Database.MigrateAsync(cancellationToken);

        FeedsDbContext feedsDb = scope.ServiceProvider.GetRequiredService<FeedsDbContext>();
        await feedsDb.Database.MigrateAsync(cancellationToken);
    }
}
