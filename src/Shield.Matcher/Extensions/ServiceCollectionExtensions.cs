using Microsoft.Extensions.DependencyInjection;
using Shield.Core.Domain;
using Shield.Matcher.Versioning;

namespace Shield.Matcher.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShieldMatcher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.Npm));
        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.Composer));
        // Swift, Pub (Dart) and Hex (Elixir) follow SemVer 2 exactly.
        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.SwiftPM));
        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.Pub));
        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.Hex));
        services.AddSingleton<IVersionComparer, NugetVersionComparer>();
        services.AddSingleton<IVersionComparer, GradleVersionComparer>();
        services.AddSingleton<IVersionComparer, MavenVersionComparer>();
        services.AddSingleton<IVersionComparer, PythonVersionComparer>();
        // TODO: Go (modules + pseudo-versions), Rust (Cargo), RubyGems (Gem::Version), Vcpkg
        // (port-version) still need dedicated comparers — none of these are pure SemVer.
        // Until added, their advisories silently DO NOT match — see the warning in
        // AdvisoryMatcher.Match.

        services.AddSingleton<AdvisoryMatcher>();
        services.AddSingleton<MaintainerDriftDetector>();

        return services;
    }
}
