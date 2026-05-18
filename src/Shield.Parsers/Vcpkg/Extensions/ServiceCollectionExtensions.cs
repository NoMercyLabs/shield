using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Vcpkg.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVcpkgParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, VcpkgJsonParser>();
        services.AddSingleton<VcpkgJsonParser>();
        return services;
    }
}
