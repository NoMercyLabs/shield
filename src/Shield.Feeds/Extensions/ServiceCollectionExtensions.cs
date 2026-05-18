using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Feeds.CratesRegistry.Extensions;
using Shield.Feeds.Epss.Extensions;
using Shield.Feeds.Ghsa.Extensions;
using Shield.Feeds.HexRegistry.Extensions;
using Shield.Feeds.Kev.Extensions;
using Shield.Feeds.NpmRegistry.Extensions;
using Shield.Feeds.NugetRegistry.Extensions;
using Shield.Feeds.Osv.Extensions;
using Shield.Feeds.PackagistRegistry.Extensions;
using Shield.Feeds.PyPiRegistry.Extensions;
using Shield.Feeds.RubyGemsRegistry.Extensions;

namespace Shield.Feeds.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldFeeds(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Advisory feeds — write to FeedsDb.Advisories.
        services.AddOsvFeed();
        services.AddGhsaFeed(configuration);
        services.AddKevFeed();
        services.AddEpssFeed();
        // Registry feeds — write to FeedsDb.PackageMetas for the anomaly detector's
        // popularity / age / maintainer signals. All 7 registries with usable public APIs.
        services.AddNpmRegistryFeed(configuration);
        services.AddNugetRegistryFeed(configuration);
        services.AddCratesRegistryFeed(configuration);
        services.AddPyPiRegistryFeed(configuration);
        services.AddRubyGemsRegistryFeed(configuration);
        services.AddPackagistRegistryFeed(configuration);
        services.AddHexRegistryFeed(configuration);
        return services;
    }
}
