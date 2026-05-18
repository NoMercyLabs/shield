using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.NpmRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNpmRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
        {
            services.Configure<NpmRegistryOptions>(
                configuration.GetSection(NpmRegistryOptions.SectionName)
            );
        }
        else
        {
            services.AddOptions<NpmRegistryOptions>();
        }

        services.AddTransient<PollyTransientHandler>();

        services
            .AddHttpClient<NpmPackageClient>(
                (sp, client) =>
                {
                    NpmRegistryOptions options = sp.GetRequiredService<IOptions<NpmRegistryOptions>>().Value;
                    client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                }
            )
            .AddHttpMessageHandler<PollyTransientHandler>();

        services.AddSingleton<IPackageMetaSink, InMemoryPackageMetaSink>();
        services.AddSingleton<IPackageNameSource, InMemoryPackageNameSource>();
        services.AddSingleton<IFeedSync, NpmRegistryFeedSync>();
        return services;
    }
}
