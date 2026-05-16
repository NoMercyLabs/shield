using Microsoft.Extensions.DependencyInjection;

namespace Shield.Alerter.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldAlerter(this IServiceCollection services)
    {
        services.AddScoped<AlertDispatcher>();
        return services;
    }
}
