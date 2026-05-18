using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Data.Tests;

public class DedupConstraintTests : IAsyncLifetime
{
    private DbConnection? _connection;
    private ShieldDbContext? _db;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        DbContextOptions<ShieldDbContext> options = new DbContextOptionsBuilder<ShieldDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateDedupKeyThrows()
    {
        await SeedParentSourceAsync();
        Finding first = NewFinding("dedup-key-1");
        _db!.Findings.Add(first);
        await _db.SaveChangesAsync();

        Finding second = NewFinding("dedup-key-1");
        _db.Findings.Add(second);

        Func<Task> save = async () => await _db.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DistinctDedupKeysPersist()
    {
        await SeedParentSourceAsync();
        _db!.Findings.Add(NewFinding("dedup-a"));
        _db.Findings.Add(NewFinding("dedup-b"));
        await _db.SaveChangesAsync();

        int count = await _db.Findings.CountAsync();
        count.Should().Be(2);
    }

    // Finding.SourceId now has a cascade FK to Sources after the orphan-cleanup migration;
    // tests need a parent Source row to satisfy the constraint. Pin Id=1 so NewFinding()
    // can reference it without reading back.
    private async Task SeedParentSourceAsync()
    {
        _db!.Sources.Add(
            new()
            {
                Id = 1,
                Type = SourceType.LocalFolder,
                Name = "test-parent",
                ConfigJson = "{}",
                ScanInterval = TimeSpan.FromHours(1),
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await _db.SaveChangesAsync();
    }

    private static Finding NewFinding(string dedupKey)
    {
        DateTime now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(),
            SourceId = 1,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = Severity.High,
            FirstSeenAt = now,
            LastSeenAt = now,
            State = FindingState.Open,
            DedupKey = dedupKey,
        };
    }
}
