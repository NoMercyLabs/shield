using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Ruby.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRubyParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, GemfileLockParser>();
        services.AddSingleton<GemfileLockParser>();
        return services;
    }
}
