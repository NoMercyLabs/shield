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
        services.AddSingleton<IVersionComparer, NugetVersionComparer>();
        services.AddSingleton<IVersionComparer, GradleVersionComparer>();

        services.AddSingleton<AdvisoryMatcher>();
        services.AddSingleton<MaintainerDriftDetector>();

        return services;
    }
}
