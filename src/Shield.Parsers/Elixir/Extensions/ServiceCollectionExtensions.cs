using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Elixir.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElixirParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, MixLockParser>();
        services.AddSingleton<MixLockParser>();
        return services;
    }
}
