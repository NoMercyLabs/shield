using Microsoft.EntityFrameworkCore;
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

        services.AddDbContext<ShieldDbContext>(options => options.UseSqlite(shieldConnection));
        services.AddDbContext<FeedsDbContext>(options => options.UseSqlite(feedsConnection));

        return services;
    }
}
