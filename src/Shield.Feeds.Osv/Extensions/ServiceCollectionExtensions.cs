using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Feeds.Osv.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOsvFeed(this IServiceCollection services)
    {
        services.AddTransient<PollyHttpRetryHandler>();

        services
            .AddHttpClient(OsvFeedSync.HttpClientName, client =>
            {
                client.BaseAddress = new Uri("https://api.osv.dev/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Shield/0.1 (+https://github.com/nomercylabs/shield)");
            })
            .AddHttpMessageHandler<PollyHttpRetryHandler>();

        services.AddTransient<IFeedSync>(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            return new OsvFeedSync(factory.CreateClient(OsvFeedSync.HttpClientName));
        });

        services.AddTransient(sp =>
        {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            return new OsvFeedSync(factory.CreateClient(OsvFeedSync.HttpClientName));
        });

        return services;
    }
}
