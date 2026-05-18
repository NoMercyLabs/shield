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

// Separate WebApplicationFactory for the deferred-entry tests so they get an isolated
// DB and don't interfere with the other ScanQueueTests fixture.
public sealed class ScanQueueWorkerDeferredFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _testRoot;
    private readonly string _shieldDb;
    private readonly string _feedsDb;

    static ScanQueueWorkerDeferredFactory()
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

    public ScanQueueWorkerDeferredFactory()
    {
        _testRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "shield-deferred-tests", Guid.NewGuid().ToString("n"))
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
                    ["Shield:Auth:DataProtectionMasterKey"] =
                        "test-data-protection-master-key-deterministic-tests-32",
                };
                config.AddInMemoryCollection(overrides);
            }
        );

        builder.ConfigureServices(services =>
        {
            string shieldConn = $"Data Source={_shieldDb}";
            string feedsConn = $"Data Source={_feedsDb}";

            RemoveDbContext<ShieldDbContext>(services);
            RemoveDbContext<FeedsDbContext>(services);
            RemoveDbContext<InboxDbContext>(services);

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

            ServiceDescriptor[] scannerDescriptors = services
                .Where(descriptor => descriptor.ServiceType == typeof(IScanner))
                .ToArray();
            foreach (ServiceDescriptor descriptor in scannerDescriptors)
            {
                if (descriptor.ImplementationType == typeof(LocalFolderScanner))
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
            // Best-effort cleanup.
        }
    }
}
