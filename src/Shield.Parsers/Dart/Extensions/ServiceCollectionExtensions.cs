using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Dart.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDartParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, PubspecLockParser>();
        services.AddSingleton<PubspecLockParser>();
        return services;
    }
}
