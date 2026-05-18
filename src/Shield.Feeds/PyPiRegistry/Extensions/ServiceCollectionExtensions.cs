using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.PyPiRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPyPiRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<PyPiRegistryOptions>(
                configuration.GetSection(PyPiRegistryOptions.SectionName)
            );
        else
            services.AddOptions<PyPiRegistryOptions>();

        services.AddHttpClient<PyPiPackageClient>(
            (sp, client) =>
            {
                PyPiRegistryOptions options = sp.GetRequiredService<
                    IOptions<PyPiRegistryOptions>
                >().Value;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<PyPiRegistryFeedSync>());
        return services;
    }
}
