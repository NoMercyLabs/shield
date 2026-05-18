using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Swift.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwiftParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, PackageResolvedParser>();
        services.AddSingleton<PackageResolvedParser>();
        return services;
    }
}
