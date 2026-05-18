using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.HexRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHexRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<HexRegistryOptions>(
                configuration.GetSection(HexRegistryOptions.SectionName)
            );
        else
            services.AddOptions<HexRegistryOptions>();

        services.AddHttpClient<HexPackageClient>(
            (sp, client) =>
            {
                HexRegistryOptions options = sp.GetRequiredService<
                    IOptions<HexRegistryOptions>
                >().Value;
                client.BaseAddress = new(options.Endpoint.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<HexRegistryFeedSync>());
        return services;
    }
}
