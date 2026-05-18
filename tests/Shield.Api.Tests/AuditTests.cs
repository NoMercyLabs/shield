using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class AuditTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public AuditTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AckFindingRecordsAuditEntryWithFindingAckAction()
    {
        Guid findingId = await SeedFindingAsync();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage ack = await client.PostAsync(
            $"/api/findings/{findingId}/ack",
            content: null
        );
        ack.StatusCode.Should().Be(HttpStatusCode.OK);

        // Audit middleware fires after the response — the row must exist by the time the
        // client receives the body since middleware awaits the logger before unwinding.
        AuditEntry? entry;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            entry = await db
                .AuditEntries.Where(row =>
                    row.Action == "finding.ack" && row.TargetId == findingId.ToString()
                )
                .OrderByDescending(row => row.At)
                .FirstOrDefaultAsync();
        }

        entry.Should().NotBeNull();
        entry!.TargetType.Should().Be("Finding");
        entry.ActorName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListReturnsAuditEntriesFilteredByAction()
    {
        Guid first = await SeedFindingAsync();
        Guid second = await SeedFindingAsync();

        HttpClient client = _factory.CreateClient();
        (await client.PostAsync($"/api/findings/{first}/ack", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/findings/{second}/resolve", null)).EnsureSuccessStatusCode();

        HttpResponseMessage list = await client.GetAsync(
            "/api/audit?action=finding.resolve&pageSize=50"
        );
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        AuditPage? body = await list.Content.ReadFromJsonAsync<AuditPage>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.Items.Should().OnlyContain(entry => entry.Action == "finding.resolve");
        body.Items.Should().Contain(entry => entry.TargetId == second.ToString());
    }

    [Fact]
    public async Task GetFindingsDoesNotRecordAuditEntry()
    {
        long beforeCount;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            beforeCount = await db.AuditEntries.LongCountAsync();
        }

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage list = await client.GetAsync("/api/findings?pageSize=10");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        long afterCount;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
            afterCount = await db.AuditEntries.LongCountAsync();
        }

        afterCount.Should().Be(beforeCount);
    }

    private async Task<Guid> SeedFindingAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        Finding finding = new()
        {
            Id = Guid.NewGuid(),
            SourceId = 9999,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = Severity.High,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = "audit-fixture-" + Guid.NewGuid().ToString("n"),
        };
        db.Findings.Add(finding);
        await db.SaveChangesAsync();
        return finding.Id;
    }
}
