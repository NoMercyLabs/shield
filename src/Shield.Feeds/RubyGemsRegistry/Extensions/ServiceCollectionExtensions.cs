using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.RubyGemsRegistry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRubyGemsRegistryFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
            services.Configure<RubyGemsRegistryOptions>(
                configuration.GetSection(RubyGemsRegistryOptions.SectionName)
            );
        else
            services.AddOptions<RubyGemsRegistryOptions>();

        services.AddHttpClient<RubyGemsPackageClient>(
            (sp, client) =>
            {
                RubyGemsRegistryOptions options = sp.GetRequiredService<
                    IOptions<RubyGemsRegistryOptions>
                >().Value;
                client.BaseAddress = new(options.Endpoint.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            }
        );

        services.AddScoped<IFeedSync>(sp => sp.GetRequiredService<RubyGemsRegistryFeedSync>());
        return services;
    }
}
