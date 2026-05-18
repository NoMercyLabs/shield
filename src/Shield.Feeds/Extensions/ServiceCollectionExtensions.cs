using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Feeds.Epss.Extensions;
using Shield.Feeds.Ghsa.Extensions;
using Shield.Feeds.Kev.Extensions;
using Shield.Feeds.NpmRegistry.Extensions;
using Shield.Feeds.Osv.Extensions;

namespace Shield.Feeds.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldFeeds(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddOsvFeed();
        services.AddGhsaFeed(configuration);
        services.AddNpmRegistryFeed(configuration);
        services.AddKevFeed();
        services.AddEpssFeed();
        return services;
    }
}
