using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Epss.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEpssFeed(this IServiceCollection services)
    {
        services.AddTransient<PollyHttpRetryHandler>();

        services
            .AddHttpClient(
                EpssFeedSync.HttpClientName,
                client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Shield/0.1 (+https://github.com/nomercylabs/shield)"
                    );
                }
            )
            .AddHttpMessageHandler<PollyHttpRetryHandler>();

        services.AddScoped<IFeedSync>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            IEpssAdvisoryEnricher enricher = sp.GetRequiredService<IEpssAdvisoryEnricher>();
            return new EpssFeedSync(factory.CreateClient(EpssFeedSync.HttpClientName), enricher);
        });

        return services;
    }
}
