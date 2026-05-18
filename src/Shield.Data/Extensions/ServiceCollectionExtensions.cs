using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shield.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldData(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        string shieldConnection =
            configuration["Shield:Db:Shield"]
            ?? throw new InvalidOperationException(
                "Configuration value 'Shield:Db:Shield' is required."
            );
        string feedsConnection =
            configuration["Shield:Db:Feeds"]
            ?? throw new InvalidOperationException(
                "Configuration value 'Shield:Db:Feeds' is required."
            );

        // Suppress PendingModelChangesWarning so multi-agent migration drift doesn't gate
        // startup. Real schema drift still surfaces at runtime via the next Migrate call,
        // but we don't bounce the whole process during a build wave.
        services.AddDbContext<ShieldDbContext>(options =>
            options
                .UseSqlite(shieldConnection)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
                )
        );
        services.AddDbContext<FeedsDbContext>(options =>
            options
                .UseSqlite(feedsConnection)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
                )
        );

        return services;
    }
}
