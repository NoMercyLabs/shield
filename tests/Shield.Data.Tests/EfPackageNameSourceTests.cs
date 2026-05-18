using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Data.Tests;

// Covers what each registry feed actually consumes from the name source: distinct package
// names per ecosystem, filtered by the IEcosystemTag generic parameter so npm sync never
// pulls nuget names by accident.
public class EfPackageNameSourceTests : IAsyncLifetime
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
    public async Task ReturnsDistinctPackageNamesForTaggedEcosystem()
    {
        Guid snapshotId = await SeedSnapshotAsync(
            (Ecosystem.Npm, "react"),
            (Ecosystem.Npm, "react-dom"),
            (Ecosystem.Npm, "react"), // duplicate inside same snapshot
            (Ecosystem.Nuget, "Microsoft.EntityFrameworkCore")
        );
        _ = snapshotId;

        EfPackageNameSource<EcosystemTag.Npm> source = new(_db!);
        IReadOnlyList<string> names = await source.GetPackageNamesAsync(CancellationToken.None);

        names.Should().BeEquivalentTo(["react", "react-dom"]);
    }

    [Fact]
    public async Task FiltersOutOtherEcosystems()
    {
        await SeedSnapshotAsync(
            (Ecosystem.Npm, "axios"),
            (Ecosystem.Nuget, "Newtonsoft.Json"),
            (Ecosystem.Rust, "serde")
        );

        EfPackageNameSource<EcosystemTag.Nuget> source = new(_db!);
        IReadOnlyList<string> names = await source.GetPackageNamesAsync(CancellationToken.None);

        names.Should().BeEquivalentTo(["Newtonsoft.Json"]);
    }

    [Fact]
    public async Task ReturnsEmptyWhenInventoryHasNoMatchingEcosystem()
    {
        await SeedSnapshotAsync((Ecosystem.Npm, "vue"));

        EfPackageNameSource<EcosystemTag.Rust> source = new(_db!);
        IReadOnlyList<string> names = await source.GetPackageNamesAsync(CancellationToken.None);

        names.Should().BeEmpty();
    }

    private async Task<Guid> SeedSnapshotAsync(params (Ecosystem Ecosystem, string Name)[] items)
    {
        Source src = new()
        {
            Type = SourceType.LocalFolder,
            Name = "seed",
            ConfigJson = "{}",
            ScanInterval = TimeSpan.FromHours(1),
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db!.Sources.Add(src);
        await _db.SaveChangesAsync();

        Guid snapshotId = Guid.NewGuid();
        _db.InventorySnapshots.Add(
            new()
            {
                Id = snapshotId,
                SourceId = src.Id,
                TakenAt = DateTime.UtcNow,
                ContentsSha = "sha",
                ItemCount = items.Length,
            }
        );
        foreach ((Ecosystem ecosystem, string name) in items)
        {
            _db.InventoryItems.Add(
                new()
                {
                    SnapshotId = snapshotId,
                    Ecosystem = ecosystem,
                    Name = name,
                    Version = "1.0.0",
                    IsDirect = true,
                }
            );
        }
        await _db.SaveChangesAsync();
        return snapshotId;
    }
}
