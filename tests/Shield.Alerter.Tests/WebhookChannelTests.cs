using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Channels.Webhook;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Alerter.Tests;

public class WebhookChannelTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceProvider _provider;

    public WebhookChannelTests()
    {
        _server = WireMockServer.Start();
        ServiceCollection services = [];
        services.AddHttpClient(WebhookChannel.HttpClientName);
        _provider = services.BuildServiceProvider();
        _httpClientFactory = _provider.GetRequiredService<IHttpClientFactory>();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Finding NewFinding(Severity severity) =>
        new()
        {
            Id = Guid.NewGuid(),
            SourceId = 1,
            InventoryItemId = 1,
            AdvisoryRefId = Guid.NewGuid(),
            Severity = severity,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            State = FindingState.Open,
            DedupKey = Guid.NewGuid().ToString("N"),
            Notes = $"finding-{severity}",
        };

    private AlertChannel NewChannel(object configObj) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Webhook,
            Name = "webhook",
            ConfigJsonEncrypted = JsonSerializer.Serialize(configObj),
            MinSeverity = Severity.Low,
            Enabled = true,
        };

    [Fact]
    public async Task DefaultBodyIsFindingJsonWithCustomHeader()
    {
        _server
            .Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(202));

        WebhookChannel channel = new(_httpClientFactory, NullLogger<WebhookChannel>.Instance);
        Finding finding = NewFinding(Severity.High);
        AlertChannel cfg = NewChannel(
            new
            {
                url = $"{_server.Urls[0]}/hook",
                headers = new Dictionary<string, string> { ["Authorization"] = "Bearer abc" },
            }
        );

        AlertResult result = await channel.SendAsync(cfg, [finding], CancellationToken.None);

        result.Success.Should().BeTrue();
        var entry = _server.LogEntries.ToList()[0];
        entry.RequestMessage.Method.Should().BeEquivalentTo("POST");
        entry
            .RequestMessage.Headers!["Authorization"]
            .Should()
            .ContainSingle(h => h == "Bearer abc");
        string body = entry.RequestMessage.Body ?? "";
        using JsonDocument doc = JsonDocument.Parse(body);
        // Default System.Text.Json serializes properties in PascalCase.
        doc.RootElement.GetProperty("DedupKey").GetString().Should().Be(finding.DedupKey);
    }

    [Fact]
    public async Task BodyTemplateRendersPlaceholders()
    {
        _server
            .Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        WebhookChannel channel = new(_httpClientFactory, NullLogger<WebhookChannel>.Instance);
        Finding finding = NewFinding(Severity.Critical);
        AlertChannel cfg = NewChannel(
            new
            {
                url = $"{_server.Urls[0]}/hook",
                bodyTemplate = "sev={{severity}} id={{findingId}} count={{count}}",
            }
        );

        AlertResult result = await channel.SendAsync(cfg, [finding], CancellationToken.None);

        result.Success.Should().BeTrue();
        string body = _server.LogEntries.ToList()[0].RequestMessage.Body ?? "";
        body.Should().Be($"sev=Critical id={finding.Id} count=1");
    }

    [Fact]
    public async Task NonSuccessHttpReturnsFailure()
    {
        _server
            .Given(Request.Create().WithPath("/hook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("nope"));

        WebhookChannel channel = new(_httpClientFactory, NullLogger<WebhookChannel>.Instance);
        AlertChannel cfg = NewChannel(new { url = $"{_server.Urls[0]}/hook" });

        AlertResult result = await channel.SendAsync(
            cfg,
            [NewFinding(Severity.Low)],
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("500");
    }
}
