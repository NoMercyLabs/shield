using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Kev.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKevFeed(this IServiceCollection services)
    {
        services.AddTransient<PollyHttpRetryHandler>();

        services
            .AddHttpClient(
                KevFeedSync.HttpClientName,
                client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(2);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Shield/0.1 (+https://github.com/nomercylabs/shield)"
                    );
                }
            )
            .AddHttpMessageHandler<PollyHttpRetryHandler>();

        // Scoped so the constructor-injected IKevAdvisoryEnricher (which holds a scoped
        // FeedsDbContext) is resolved correctly per sync run.
        services.AddScoped<IFeedSync>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            IKevAdvisoryEnricher enricher = sp.GetRequiredService<IKevAdvisoryEnricher>();
            return new KevFeedSync(factory.CreateClient(KevFeedSync.HttpClientName), enricher);
        });

        return services;
    }
}
