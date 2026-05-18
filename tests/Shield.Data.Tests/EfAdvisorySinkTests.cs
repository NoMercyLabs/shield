using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Xunit;

namespace Shield.Data.Tests;

public class EfAdvisorySinkTests : IAsyncLifetime
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
    public async Task UpsertAsyncPersistsNewAdvisoriesIntoFeedsDb()
    {
        EfAdvisorySink sink = new(_db!);
        Advisory incoming = MakeAdvisory(
            Feed.Osv,
            "GHSA-test-aaaa",
            "lodash",
            "[{\"events\":[{\"introduced\":\"0\"},{\"fixed\":\"4.17.21\"}]}]"
        );

        await sink.UpsertAsync([incoming], CancellationToken.None);

        List<Advisory> rows = await _db!.Advisories.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].ExternalId.Should().Be("GHSA-test-aaaa");
        rows[0].PackageName.Should().Be("lodash");
        rows[0].Feed.Should().Be(Feed.Osv);
    }

    [Fact]
    public async Task UpsertAsyncIsIdempotentOnFeedAndExternalId()
    {
        EfAdvisorySink sink = new(_db!);
        Advisory first = MakeAdvisory(Feed.Osv, "GHSA-test-bbbb", "lodash", "[]");
        await sink.UpsertAsync([first], CancellationToken.None);

        Advisory updated = MakeAdvisory(
            Feed.Osv,
            "GHSA-test-bbbb",
            "lodash",
            "[{\"events\":[{\"introduced\":\"0\"}]}]"
        );
        updated.Summary = "updated summary";
        await sink.UpsertAsync([updated], CancellationToken.None);

        List<Advisory> rows = await _db!.Advisories.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Summary.Should().Be("updated summary");
        rows[0].AffectedRangesJson.Should().Contain("introduced");
    }

    private static Advisory MakeAdvisory(
        Feed feed,
        string externalId,
        string name,
        string rangesJson
    )
    {
        DateTime now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(),
            Feed = feed,
            ExternalId = externalId,
            Ecosystem = Ecosystem.Npm,
            PackageName = name,
            AffectedRangesJson = rangesJson,
            Severity = Severity.Critical,
            Summary = "test advisory",
            ReferencesJson = "[]",
            PublishedAt = now,
            ModifiedAt = now,
            FetchedAt = now,
        };
    }
}
