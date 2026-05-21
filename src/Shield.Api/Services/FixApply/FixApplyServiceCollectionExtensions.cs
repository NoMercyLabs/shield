using Shield.Api.Services.BulkFix;
using Shield.Api.Services.ManifestEditors;
using Shield.Api.Services.PullRequests;
using Shield.Api.Services.SourceFs;

namespace Shield.Api.Services.FixApply;

// Registers the advisory-driven fix-application pipeline:
//   - FixSuggester picks target versions per advisory
//   - Per-ecosystem manifest editors patch package files
//   - FixApplier (single-finding) + BulkFixApplier (whole-source) drive the flow
//   - IRepoPullRequestOpener + IRepoSourceFs are shared with the Updates apply path
public static class FixApplyServiceCollectionExtensions
{
    public static IServiceCollection AddShieldFixApply(this IServiceCollection services)
    {
        services.AddSingleton<IFixSuggester, FixSuggester>();

        services.AddSingleton<NpmManifestEditor>();
        services.AddSingleton<NugetManifestEditor>();
        services.AddSingleton<ComposerManifestEditor>();
        services.AddSingleton<GradleManifestEditor>();
        services.AddSingleton<PythonManifestEditor>();
        services.AddSingleton<GoManifestEditor>();
        services.AddSingleton<RustManifestEditor>();

        services.AddScoped<IFixApplier, FixApplier>();
        services.AddScoped<IBulkFixApplier, BulkFixApplier>();
        services.AddScoped<IBulkApplyOrchestrator, BulkApplyOrchestrator>();
        services.AddScoped<IRepoPullRequestOpener, GithubPullRequestOpener>();
        services.AddScoped<IRepoSourceFs, GithubRepoSourceFs>();

        // Dependabot conflict lookup uses a typed HttpClient with no DefaultRequestHeaders —
        // we set Authorization per call against the live OAuth bearer at request time.
        services.AddHttpClient<IDependabotPrLookup, GithubDependabotPrLookup>();
        return services;
    }
}
