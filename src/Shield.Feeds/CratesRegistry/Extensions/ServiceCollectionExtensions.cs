using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.CratesRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCratesRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<CratesRegistryOptions>(
                configuration.GetSection(CratesRegistryOptions.SectionName)
            );
        else
            services.AddOptions<CratesRegistryOptions>();

        services.AddHttpClient<CratesPackageClient>(
            (sp, client) =>
            {
                CratesRegistryOptions options = sp.GetRequiredService<
                    IOptions<CratesRegistryOptions>
                >().Value;
                client.BaseAddress = new(options.Endpoint.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<CratesRegistryFeedSync>());
        return services;
    }
}
