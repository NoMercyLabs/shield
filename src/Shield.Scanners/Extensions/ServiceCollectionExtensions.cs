using Microsoft.Extensions.DependencyInjection;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Parsers.Composer;
using Shield.Parsers.Gradle;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;

namespace Shield.Scanners.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldScanners(this IServiceCollection services)
    {
        services.TryAddSingleton(new ProductHeaderValue("Shield"));
        services.AddSingleton<IGitHubClient>(provider =>
            new GitHubClient(provider.GetRequiredService<ProductHeaderValue>())
        );

        services.AddSingleton<NpmLockParser>();
        services.AddSingleton<NugetLockParser>();
        services.AddSingleton<ComposerLockParser>();
        services.AddSingleton<GradleLockfileParser>();

        services.AddSingleton<ParserRegistry>();
        services.AddSingleton<IScanner, LocalFolderScanner>();
        services.AddSingleton<IScanner, GitHubRepoScanner>();
        services.AddSingleton<ScannerRegistry>();
        return services;
    }

    static void TryAddSingleton<T>(this IServiceCollection services, T instance)
        where T : class
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
            return;
        services.AddSingleton(instance);
    }
}
