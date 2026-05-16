using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Domain;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class FindingsTests : IClassFixture<ShieldWebAppFactory>
{
    private readonly ShieldWebAppFactory _factory;

    public FindingsTests(ShieldWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ack_flips_finding_state_to_acked()
    {
        Guid id = await SeedFindingAsync();

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage ack = await client.PostAsync($"/api/findings/{id}/ack", content: null);
        ack.StatusCode.Should().Be(HttpStatusCode.OK);

        FindingResponse? result = await ack.Content.ReadFromJsonAsync<FindingResponse>();
        result.Should().NotBeNull();
        result!.State.Should().Be(FindingState.Acked);
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
            DedupKey = "fixture-" + Guid.NewGuid().ToString("n"),
        };
        db.Findings.Add(finding);
        await db.SaveChangesAsync();
        return finding.Id;
    }
}
