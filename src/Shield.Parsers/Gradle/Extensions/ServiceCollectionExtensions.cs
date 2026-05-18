using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Gradle.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGradleParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, GradleLockfileParser>();
        services.AddSingleton<GradleLockfileParser>();
        return services;
    }
}
