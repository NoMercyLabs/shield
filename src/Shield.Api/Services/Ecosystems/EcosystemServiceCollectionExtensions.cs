namespace Shield.Api.Services.Ecosystems;

// Registers one IEcosystem per supported language. Adding a new ecosystem = drop a new class
// under Services/Ecosystems/ and add one call here. The dispatcher (EcosystemRegistry) resolves
// them by Ecosystem enum value at runtime.
public static class EcosystemServiceCollectionExtensions
{
    private const string UserAgent = "Shield-UpdatesProbe/1.0";

    public static IServiceCollection AddShieldEcosystems(this IServiceCollection services)
    {
        services.AddHttpClient<INugetRegistryProbe, NugetRegistryProbe>(client =>
            ConfigureClient(client, "https://api.nuget.org/")
        );

        services.AddScoped<IEcosystem, NpmEcosystem>();
        services.AddScoped<IEcosystem, NugetEcosystem>();

        services.AddHttpClient<IEcosystem, ComposerEcosystem>(client =>
            ConfigureClient(client, "https://repo.packagist.org/")
        );
        services.AddHttpClient<IEcosystem, PythonEcosystem>(client =>
            ConfigureClient(client, "https://pypi.org/")
        );

        // Maven Central is shared by Maven + Gradle. MavenEcosystem owns the HttpClient;
        // GradleEcosystem wraps it so the dispatcher resolves both ecosystems.
        services.AddHttpClient<MavenEcosystem>(client =>
            ConfigureClient(client, "https://search.maven.org/")
        );
        services.AddScoped<IEcosystem>(sp => sp.GetRequiredService<MavenEcosystem>());
        services.AddScoped<IEcosystem, GradleEcosystem>();

        services.AddHttpClient<IEcosystem, RustEcosystem>(client =>
        {
            ConfigureClient(client, "https://crates.io/");
            // crates.io rejects requests without a contact-email UA.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("(shield-dev@nomercy.tv)");
        });
        services.AddHttpClient<IEcosystem, GoEcosystem>(client =>
            ConfigureClient(client, "https://proxy.golang.org/")
        );
        services.AddHttpClient<IEcosystem, RubyGemsEcosystem>(client =>
            ConfigureClient(client, "https://rubygems.org/")
        );
        services.AddHttpClient<IEcosystem, HexEcosystem>(client =>
            ConfigureClient(client, "https://hex.pm/")
        );
        services.AddHttpClient<IEcosystem, PubEcosystem>(client =>
            ConfigureClient(client, "https://pub.dev/")
        );
        services.AddHttpClient<IEcosystem, SwiftPmEcosystem>(client =>
        {
            ConfigureClient(client, "https://api.github.com/");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        services.AddHttpClient<IEcosystem, VcpkgEcosystem>(client =>
            ConfigureClient(client, "https://raw.githubusercontent.com/")
        );

        services.AddScoped<IEcosystemRegistry, EcosystemRegistry>();
        return services;
    }

    private static void ConfigureClient(HttpClient client, string baseAddress)
    {
        client.BaseAddress = new(baseAddress);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
