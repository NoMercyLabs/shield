using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Ghsa.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGhsaFeed(
        this IServiceCollection services,
        IConfiguration? configuration = null
    )
    {
        if (configuration is not null)
        {
            services.Configure<GhsaOptions>(configuration.GetSection(GhsaOptions.SectionName));
        }
        else
        {
            services.AddOptions<GhsaOptions>();
        }

        services.AddTransient<PollyTransientHandler>();

        services
            .AddHttpClient<GhsaGraphQLClient>(
                (sp, client) =>
                {
                    GhsaOptions options = sp.GetRequiredService<IOptions<GhsaOptions>>().Value;
                    client.BaseAddress = new Uri(options.Endpoint);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                    if (!string.IsNullOrWhiteSpace(options.Pat))
                    {
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                            "Bearer",
                            options.Pat
                        );
                    }
                }
            )
            .AddHttpMessageHandler<PollyTransientHandler>();

        services.AddSingleton<IAdvisorySink, InMemoryAdvisorySink>();
        services.AddSingleton<IFeedSync, GhsaFeedSync>();
        return services;
    }
}
