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
        services.AddTransient<GhsaAuthHandler>();

        // Default token source reads GhsaOptions.Pat from config. Shield.Api overrides
        // this binding (Replace) with OAuthBackedGhsaTokenSource so a logged-in GitHub
        // user is enough — no separate PAT required.
        services.AddSingleton<IGhsaAuthTokenSource, GhsaPatTokenSource>();

        services
            .AddHttpClient<GhsaGraphQLClient>(
                (sp, client) =>
                {
                    GhsaOptions options = sp.GetRequiredService<IOptions<GhsaOptions>>().Value;
                    client.BaseAddress = new Uri(options.Endpoint);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                }
            )
            .AddHttpMessageHandler<GhsaAuthHandler>()
            .AddHttpMessageHandler<PollyTransientHandler>();

        services.AddSingleton<IAdvisorySink, InMemoryAdvisorySink>();
        services.AddSingleton<IFeedSync, GhsaFeedSync>();
        return services;
    }
}
