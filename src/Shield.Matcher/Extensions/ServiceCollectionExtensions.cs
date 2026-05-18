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
        services.AddSingleton<IVersionComparer, GemVersionComparer>();
        services.AddSingleton<IVersionComparer, GoModVersionComparer>();
        // Cargo (Rust) is SemVer 2.0 exactly for version COMPARISON — its deviations are in
        // dependency RESOLUTION syntax (^1.2, ~1.2, wildcards in Cargo.toml) which only matters
        // for manifest parsing, not advisory range matching.
        services.AddSingleton<IVersionComparer>(_ => new SemverVersionComparer(Ecosystem.Rust));
        services.AddSingleton<IVersionComparer, VcpkgVersionComparer>();

        services.AddSingleton<AdvisoryMatcher>();
        services.AddSingleton<MaintainerDriftDetector>();

        return services;
    }
}
