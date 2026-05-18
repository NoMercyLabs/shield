using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;
using Shield.Core.Domain;

namespace Shield.Feeds.NpmRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    // EF-backed sinks must be registered separately by the host project (Shield.Api), which
    // is where the DbContext lives. This extension only wires the npm-specific HTTP client
    // and the sync class — the sink and name source resolve from whatever the host bound.
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
                    NpmRegistryOptions options = sp.GetRequiredService<
                        IOptions<NpmRegistryOptions>
                    >().Value;
                    client.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                }
            )
            .AddHttpMessageHandler<PollyTransientHandler>();

        // The host (Shield.Api) registers NpmRegistryFeedSync itself via a factory that
        // constructs an EfPackageNameSource bound to Ecosystem.Npm — the IPackageNameSource
        // abstraction is ecosystem-specific so it can't be a globally-shared singleton.
        // We just expose the type via IFeedSync so FeedSyncWorker can discover it.
        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<NpmRegistryFeedSync>());
        return services;
    }
}
