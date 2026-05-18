using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

// Verifies that all EF migrations apply cleanly to a fresh SQLite file and that
// every entity registered on ShieldDbContext has its backing table.  The test
// uses MigrateAsync (not EnsureCreated) so the migration chain — not just the
// current model snapshot — must be internally consistent.
public sealed class MigrationTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;
    private string _feedsDbPath = string.Empty;

    public Task InitializeAsync()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            "shield-migration-tests",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "shield.db");
        _feedsDbPath = Path.Combine(tempDir, "feeds.db");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort; SQLite may still hold a handle briefly.
        }
        return Task.CompletedTask;
    }

    private ShieldDbContext BuildShieldContext()
    {
        DbContextOptions<ShieldDbContext> options = new DbContextOptionsBuilder<ShieldDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;
        return new ShieldDbContext(options);
    }

    private FeedsDbContext BuildFeedsContext()
    {
        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseSqlite($"Data Source={_feedsDbPath}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;
        return new FeedsDbContext(options);
    }

    [Fact]
    public async Task Shield_migrations_apply_cleanly_to_fresh_database()
    {
        await using ShieldDbContext db = BuildShieldContext();

        Func<Task> migrate = async () => await db.Database.MigrateAsync();

        await migrate.Should().NotThrowAsync("all Shield migrations must apply in order");
    }

    [Fact]
    public async Task Feeds_migrations_apply_cleanly_to_fresh_database()
    {
        await using FeedsDbContext db = BuildFeedsContext();

        Func<Task> migrate = async () => await db.Database.MigrateAsync();

        await migrate.Should().NotThrowAsync("all Feeds migrations must apply in order");
    }

    [Fact]
    public async Task Shield_all_entity_tables_exist_after_migration()
    {
        await using ShieldDbContext db = BuildShieldContext();
        await db.Database.MigrateAsync();

        string[] expectedTables =
        [
            "Sources",
            "InventorySnapshots",
            "InventoryItems",
            "Findings",
            "AlertChannels",
            "AlertEvents",
            "AgentTokens",
            "AppSettings",
            "IntegrationTokens",
            "AuditEntries",
            "SourceGroups",
            "SourceAccesses",
            "GroupMemberships",
            "UserSessions",
            "Notifications",
            "PackageWatches",
            "SavedFilters",
            "ApiTokens",
            "ScanQueueEntries",
            "Invites",
            "PushSubscriptions",
            "SecurityEvents",
            "IpReputations",
            // ASP.NET Core Identity tables
            "AspNetUsers",
            "AspNetRoles",
            "AspNetUserRoles",
            "AspNetUserClaims",
            "AspNetRoleClaims",
            "AspNetUserLogins",
            "AspNetUserTokens",
        ];

        string connectionString = $"Data Source={_dbPath}";
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();

        foreach (string tableName in expectedTables)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
            command.Parameters.AddWithValue("@name", tableName);
            long count = (long)(await command.ExecuteScalarAsync())!;
            count.Should().Be(1, $"table '{tableName}' must exist after migrations");
        }
    }

    [Fact]
    public async Task Shield_migrations_are_idempotent_on_second_apply()
    {
        // Running MigrateAsync twice must not throw — the __EFMigrationsHistory table
        // records what has been applied, so re-running against an already-migrated DB
        // is a no-op.
        await using ShieldDbContext first = BuildShieldContext();
        await first.Database.MigrateAsync();

        await using ShieldDbContext second = BuildShieldContext();
        Func<Task> secondMigrate = async () => await second.Database.MigrateAsync();
        await secondMigrate.Should().NotThrowAsync("migrations must be idempotent");
    }

    [Fact]
    public async Task Shield_migration_history_records_all_migrations()
    {
        await using ShieldDbContext db = BuildShieldContext();
        await db.Database.MigrateAsync();

        IEnumerable<string> applied = await db.Database.GetAppliedMigrationsAsync();

        applied.Should().Contain("20260516033107_Initial");
        applied.Should().Contain("20260516094246_AddApiTokens");
        applied.Should().Contain("20260516093058_AddUserSessions");
        applied.Should().Contain("20260516170000_AddPushSubscriptions");
        applied.Should().Contain("20260516180000_AddSecurityEvents");
    }
}
