using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Npm.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNpmParser(this IServiceCollection services)
    {
        services.AddKeyedSingleton<IParser, NpmLockParser>("npm");
        return services;
    }
}
