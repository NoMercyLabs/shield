using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Maven.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMavenParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, PomXmlParser>();
        services.AddSingleton<PomXmlParser>();
        return services;
    }
}
