using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Go.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGoParser(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IParser, GoDependencyParser>("go");
        return services;
    }
}
