using Microsoft.Extensions.DependencyInjection;
using Octokit;
using Shield.Core.Abstractions;
using Shield.Parsers.Composer;
using Shield.Parsers.Dart;
using Shield.Parsers.Elixir;
using Shield.Parsers.Go;
using Shield.Parsers.Gradle;
using Shield.Parsers.Maven;
using Shield.Parsers.Npm;
using Shield.Parsers.Nuget;
using Shield.Parsers.Python;
using Shield.Parsers.Ruby;
using Shield.Parsers.Rust;
using Shield.Parsers.Swift;
using Shield.Parsers.Vcpkg;

namespace Shield.Scanners.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldScanners(this IServiceCollection services)
    {
        services.TryAddSingleton(new ProductHeaderValue("Shield"));
        // Default anonymous client — only used when the Shield.Api host hasn't registered a
        // token-aware IGitHubScannerClientFactory. Production wires that factory in Program.cs
        // and the singleton below is shadowed.
        services.AddSingleton<IGitHubClient>(provider => new GitHubClient(
            provider.GetRequiredService<ProductHeaderValue>()
        ));
        services.AddSingleton<IGitHubScannerClientFactory, AnonymousGitHubScannerClientFactory>();

        services.AddSingleton<NpmLockParser>();
        services.AddSingleton<NugetDependencyParser>();
        services.AddSingleton<ComposerLockParser>();
        services.AddSingleton<GradleLockfileParser>();
        services.AddSingleton<PythonDependencyParser>();
        services.AddSingleton<GoDependencyParser>();
        services.AddSingleton<RustDependencyParser>();
        services.AddSingleton<GemfileLockParser>();
        services.AddSingleton<PackageResolvedParser>();
        services.AddSingleton<PubspecLockParser>();
        services.AddSingleton<PomXmlParser>();
        services.AddSingleton<MixLockParser>();
        services.AddSingleton<VcpkgJsonParser>();

        services.AddSingleton<ParserRegistry>();
        services.AddSingleton<IScanner, LocalFolderScanner>();
        services.AddSingleton<IScanner, GitHubRepoScanner>();
        services.AddSingleton<ScannerRegistry>();
        return services;
    }

    private static void TryAddSingleton<T>(this IServiceCollection services, T instance)
        where T : class
    {
        if (services.Any(d => d.ServiceType == typeof(T)))
            return;
        services.AddSingleton(instance);
    }
}
