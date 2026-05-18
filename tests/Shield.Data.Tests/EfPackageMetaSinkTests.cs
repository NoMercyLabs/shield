using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Data.Tests;

// Covers the upsert semantics the anomaly detector depends on: dedup-by-unique-key, never
// regress WeeklyDownloads to null on a later sync that doesn't carry it, replace mutable
// metadata (maintainers, deprecated, tarball-sha) with the freshest values.
public class EfPackageMetaSinkTests : IAsyncLifetime
{
    private DbConnection? _connection;
    private FeedsDbContext? _db;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        DbContextOptions<FeedsDbContext> options = new DbContextOptionsBuilder<FeedsDbContext>()
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
    public async Task UpsertAsyncInsertsNewPackageMetaRows()
    {
        EfPackageMetaSink sink = new(_db!);
        PackageMeta one = Make(Ecosystem.Npm, "left-pad", "1.3.0", weeklyDownloads: 5_000_000);
        PackageMeta two = Make(Ecosystem.Npm, "left-pad", "1.4.0", weeklyDownloads: 5_000_000);

        await sink.UpsertAsync([one, two], CancellationToken.None);

        List<PackageMeta> rows = await _db!.PackageMetas.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().Contain(meta => meta.Version == "1.3.0");
        rows.Should().Contain(meta => meta.Version == "1.4.0");
    }

    [Fact]
    public async Task UpsertAsyncUpdatesExistingRowMetadataInPlace()
    {
        EfPackageMetaSink sink = new(_db!);
        PackageMeta original = Make(
            Ecosystem.Npm,
            "react",
            "18.2.0",
            maintainers: ["fb"],
            weeklyDownloads: 10_000_000
        );
        await sink.UpsertAsync([original], CancellationToken.None);

        PackageMeta refreshed = Make(
            Ecosystem.Npm,
            "react",
            "18.2.0",
            maintainers: ["fb", "vercel"],
            weeklyDownloads: 25_000_000,
            deprecated: true
        );
        await sink.UpsertAsync([refreshed], CancellationToken.None);

        List<PackageMeta> rows = await _db!.PackageMetas.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].WeeklyDownloads.Should().Be(25_000_000);
        rows[0].Deprecated.Should().BeTrue();
        rows[0].MaintainersJson.Should().Contain("vercel");
    }

    [Fact]
    public async Task UpsertAsyncDoesNotWipeWeeklyDownloadsWhenIncomingValueIsNull()
    {
        // The detector floor query relies on WeeklyDownloads surviving across syncs from
        // feeds that don't carry download data (e.g. an ecosystem with no popularity API
        // running after the npm registry sync set the value).
        EfPackageMetaSink sink = new(_db!);
        PackageMeta withDownloads = Make(
            Ecosystem.Npm,
            "axios",
            "1.7.0",
            weeklyDownloads: 80_000_000
        );
        await sink.UpsertAsync([withDownloads], CancellationToken.None);

        PackageMeta withoutDownloads = Make(Ecosystem.Npm, "axios", "1.7.0", weeklyDownloads: null);
        await sink.UpsertAsync([withoutDownloads], CancellationToken.None);

        PackageMeta? row = await _db!.PackageMetas.AsNoTracking().FirstOrDefaultAsync();
        row.Should().NotBeNull();
        row!.WeeklyDownloads.Should().Be(80_000_000);
    }

    [Fact]
    public async Task UpsertAsyncDedupsDuplicateIncomingRows()
    {
        // npm registry doc can legitimately list the same version twice (a tag pointing at
        // the version label). The sink dedups in-memory before SaveChanges to avoid blowing
        // the (Ecosystem, Name, Version) unique index.
        EfPackageMetaSink sink = new(_db!);
        PackageMeta first = Make(Ecosystem.Npm, "lodash", "4.17.21", weeklyDownloads: 500_000_000);
        PackageMeta dup = Make(Ecosystem.Npm, "lodash", "4.17.21", weeklyDownloads: 500_000_000);

        await sink.UpsertAsync([first, dup], CancellationToken.None);

        int count = await _db!.PackageMetas.CountAsync();
        count.Should().Be(1);
    }

    private static PackageMeta Make(
        Ecosystem ecosystem,
        string name,
        string version,
        IReadOnlyList<string>? maintainers = null,
        long? weeklyDownloads = null,
        bool deprecated = false
    )
    {
        string maintainersJson = System.Text.Json.JsonSerializer.Serialize(maintainers ?? []);
        return new()
        {
            Id = Guid.NewGuid(),
            Ecosystem = ecosystem,
            Name = name,
            Version = version,
            MaintainersJson = maintainersJson,
            WeeklyDownloads = weeklyDownloads,
            Deprecated = deprecated,
            FetchedAt = DateTime.UtcNow,
        };
    }
}
