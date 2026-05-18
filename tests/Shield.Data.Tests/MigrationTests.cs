using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shield.Data;
using Xunit;

namespace Shield.Data.Tests;

public class MigrationTests : IAsyncLifetime
{
    private DbConnection? _shieldConnection;
    private DbConnection? _feedsConnection;
    private ShieldDbContext? _shieldDb;
    private FeedsDbContext? _feedsDb;

    public async Task InitializeAsync()
    {
        _shieldConnection = new SqliteConnection("DataSource=:memory:");
        await _shieldConnection.OpenAsync();
        _feedsConnection = new SqliteConnection("DataSource=:memory:");
        await _feedsConnection.OpenAsync();

        DbContextOptions<ShieldDbContext> shieldOptions = new DbContextOptionsBuilder<ShieldDbContext>()
            .UseSqlite(_shieldConnection)
            .Options;
        _shieldDb = new(shieldOptions);
        await _shieldDb.Database.MigrateAsync();

        DbContextOptions<FeedsDbContext> feedsOptions = new DbContextOptionsBuilder<FeedsDbContext>()
            .UseSqlite(_feedsConnection)
            .Options;
        _feedsDb = new(feedsOptions);
        await _feedsDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_shieldDb is not null) await _shieldDb.DisposeAsync();
        if (_feedsDb is not null) await _feedsDb.DisposeAsync();
        if (_shieldConnection is not null) await _shieldConnection.DisposeAsync();
        if (_feedsConnection is not null) await _feedsConnection.DisposeAsync();
    }

    [Fact]
    public async Task ShieldContext_CreatesExpectedTables()
    {
        IEnumerable<string> tables = await GetTableNamesAsync(_shieldConnection!);
        tables.Should().Contain([
            "Sources",
            "InventorySnapshots",
            "InventoryItems",
            "Findings",
            "AlertChannels",
            "AlertEvents",
            "AgentTokens",
            "AspNetUsers",
            "AspNetRoles"
        ]);
    }

    [Fact]
    public async Task FeedsContext_CreatesExpectedTables()
    {
        IEnumerable<string> tables = await GetTableNamesAsync(_feedsConnection!);
        tables.Should().Contain([
            "Advisories",
            "PackageMetas",
            "FeedSyncStates"
        ]);
    }

    [Fact]
    public async Task ShieldContext_FindingDedupKeyIndexIsUnique()
    {
        IEnumerable<string> indexes = await GetIndexesAsync(_shieldConnection!, "Findings");
        indexes.Should().Contain(name => name.Contains("DedupKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FeedsContext_AdvisoryFeedExternalIdIndexExists()
    {
        IEnumerable<string> indexes = await GetIndexesAsync(_feedsConnection!, "Advisories");
        indexes.Should().Contain(name => name.Contains("Feed", StringComparison.OrdinalIgnoreCase) && name.Contains("ExternalId", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<List<string>> GetTableNamesAsync(DbConnection connection)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        List<string> tables = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static async Task<List<string>> GetIndexesAsync(DbConnection connection, string table)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='{table}'";
        List<string> indexes = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }
}
