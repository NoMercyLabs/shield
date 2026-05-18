using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.NugetRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNugetRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<NugetRegistryOptions>(
                configuration.GetSection(NugetRegistryOptions.SectionName)
            );
        else
            services.AddOptions<NugetRegistryOptions>();

        services.AddHttpClient<NugetPackageClient>(
            (sp, client) =>
            {
                NugetRegistryOptions options = sp.GetRequiredService<
                    IOptions<NugetRegistryOptions>
                >().Value;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<NugetRegistryFeedSync>());
        return services;
    }
}
