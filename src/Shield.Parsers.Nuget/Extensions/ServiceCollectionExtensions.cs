using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Nuget.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNugetParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, NugetLockParser>();
        services.AddSingleton<NugetLockParser>();
        return services;
    }
}
