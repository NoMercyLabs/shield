using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Persistence;
using Shield.Core.Abstractions;
using Shield.Data;
using Shield.Scanners;
using Xunit;

namespace Shield.Api.Tests;

public class ShieldWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _testRoot;
    private readonly string _shieldDb;
    private readonly string _feedsDb;

    // Program.Main reads configuration BEFORE ConfigureAppConfiguration runs, so the
    // data-protection master-key requirement (non-Development gate) must be satisfied
    // via environment variables rather than the in-memory overrides below. Done in a
    // static ctor so it fires before any test class instantiates a factory in parallel.
    static ShieldWebAppFactory()
    {
        Environment.SetEnvironmentVariable(
            "Shield__Auth__DataProtectionMasterKey",
            "test-data-protection-master-key-deterministic-tests-32"
        );
        Environment.SetEnvironmentVariable(
            "Shield__Auth__JwtSigningKey",
            "test-signing-key-must-be-at-least-32-characters-long"
        );
    }

    public ShieldWebAppFactory()
    {
        _testRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "shield-tests", Guid.NewGuid().ToString("n"))
        );
        Directory.CreateDirectory(_testRoot);
        _shieldDb = Path.Combine(_testRoot, "shield.db");
        _feedsDb = Path.Combine(_testRoot, "feeds.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(
            (_, config) =>
            {
                Dictionary<string, string?> overrides = new()
                {
                    ["Shield:SingleUser"] = "true",
                    ["Shield:Db:Shield"] = $"Data Source={_shieldDb}",
                    ["Shield:Db:Feeds"] = $"Data Source={_feedsDb}",
                    ["Shield:OpenApi:Enabled"] = "false",
                    ["Shield:Auth:JwtSigningKey"] =
                        "test-signing-key-must-be-at-least-32-characters-long",
                    // Non-Development envs require a master key for the data-protection chain.
                    // Tests run in "Testing" so the key must be supplied here.
                    ["Shield:Auth:DataProtectionMasterKey"] =
                        "test-data-protection-master-key-deterministic-tests-32",
                };
                config.AddInMemoryCollection(overrides);
            }
        );

        builder.ConfigureServices(services =>
        {
            // AddShieldData captures the connection string at registration time, so re-register
            // the DbContexts here with the test-scoped SQLite files.
            string shieldConn = $"Data Source={_shieldDb}";
            string feedsConn = $"Data Source={_feedsDb}";

            RemoveDbContext<ShieldDbContext>(services);
            RemoveDbContext<FeedsDbContext>(services);
            RemoveDbContext<InboxDbContext>(services);

            // Suppress PendingModelChangesWarning — test DBs are created from scratch on every
            // run so snapshot/model drift in dev migrations is irrelevant here.
            services.AddDbContext<ShieldDbContext>(options =>
                options
                    .UseSqlite(shieldConn)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
                    )
            );
            services.AddDbContext<FeedsDbContext>(options =>
                options
                    .UseSqlite(feedsConn)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
                    )
            );
            services.AddDbContext<InboxDbContext>(options =>
                options
                    .UseSqlite(shieldConn)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
                    )
            );

            // Swap LocalFolder scanner for FakeScanner deterministically.
            ServiceDescriptor[] scannerDescriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IScanner))
                .ToArray();
            foreach (ServiceDescriptor descriptor in scannerDescriptors)
            {
                Type? implType = descriptor.ImplementationType;
                if (implType == typeof(LocalFolderScanner))
                    services.Remove(descriptor);
            }
            services.AddSingleton<IScanner, FakeScanner>();
        });
    }

    private static void RemoveDbContext<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        ServiceDescriptor[] toRemove = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<TContext>)
                || descriptor.ServiceType == typeof(TContext)
            )
            .ToArray();
        foreach (ServiceDescriptor descriptor in toRemove)
            services.Remove(descriptor);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — SQLite file may still be locked momentarily.
        }
    }
}
