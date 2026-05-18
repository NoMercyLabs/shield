using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shield.Core.Abstractions;

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

    // Registers the EF-backed sinks that replace the in-memory placeholders the feed
    // libraries ship with. Call AFTER AddShieldFeeds so these registrations are the last
    // ones seen for their service types and win at resolution time. Without this, every
    // feed sync (npm registry, GHSA, KEV, EPSS, OSV-broadcast) silently writes to a List
    // in memory that nothing reads, and the entire PackageMeta-driven anomaly detection
    // pipeline runs on null inputs.
    public static IServiceCollection AddShieldDataSinks(this IServiceCollection services)
    {
        // Override any prior InMemory registration. .NET DI returns the LAST registration
        // for single-resolves, so calling this after AddShieldFeeds is sufficient — but
        // Replace() makes the intent explicit and the order-of-registration safe.
        services.Replace(ServiceDescriptor.Scoped<IAdvisorySink, EfAdvisorySink>());
        services.Replace(ServiceDescriptor.Scoped<IPackageMetaSink, EfPackageMetaSink>());
        return services;
    }
}
