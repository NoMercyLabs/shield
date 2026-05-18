using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.PackagistRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPackagistRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<PackagistRegistryOptions>(
                configuration.GetSection(PackagistRegistryOptions.SectionName)
            );
        else
            services.AddOptions<PackagistRegistryOptions>();

        services.AddHttpClient<PackagistPackageClient>(
            (sp, client) =>
            {
                PackagistRegistryOptions options = sp.GetRequiredService<
                    IOptions<PackagistRegistryOptions>
                >().Value;
                client.BaseAddress = new(options.Endpoint.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<PackagistRegistryFeedSync>());
        return services;
    }
}
