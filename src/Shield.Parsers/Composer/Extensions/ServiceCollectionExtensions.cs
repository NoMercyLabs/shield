using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Abstractions;

namespace Shield.Parsers.Composer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComposerParser(this IServiceCollection services)
    {
        services.AddSingleton<IParser, ComposerLockParser>();
        services.AddSingleton<ComposerLockParser>();
        return services;
    }
}
