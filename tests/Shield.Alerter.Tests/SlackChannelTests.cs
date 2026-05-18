using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Channels.Slack;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Alerter.Tests;

public class SlackChannelTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceProvider _provider;

    public SlackChannelTests()
    {
        _server = WireMockServer.Start();
        ServiceCollection services = [];
        services.AddHttpClient(SlackChannel.HttpClientName);
        _provider = services.BuildServiceProvider();
        _httpClientFactory = _provider.GetRequiredService<IHttpClientFactory>();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _provider.Dispose();
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

    private AlertChannel NewChannel() =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Slack,
            Name = "slack",
            ConfigJsonEncrypted = JsonSerializer.Serialize(
                new { webhookUrl = $"{_server.Urls[0]}/services/T/B/abc" }
            ),
            MinSeverity = Severity.Low,
            Enabled = true,
        };

    [Fact]
    public async Task SingleFindingPostsBlockKitWithSeverityColor()
    {
        _server
            .Given(Request.Create().WithPath("/services/T/B/abc").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        SlackChannel channel = new(_httpClientFactory, NullLogger<SlackChannel>.Instance);
        Finding finding = NewFinding(Severity.Critical);

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [finding],
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Delivered.Should().Be(1);

        var logs = _server.LogEntries.ToList();
        logs.Should().HaveCount(1);
        string body = logs[0].RequestMessage.Body ?? "";
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement blocks = doc.RootElement.GetProperty("blocks");
        blocks.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        blocks[0].GetProperty("type").GetString().Should().Be("header");
        blocks[0].GetProperty("text").GetProperty("text").GetString().Should().Contain("Critical");

        JsonElement attachments = doc.RootElement.GetProperty("attachments");
        attachments[0].GetProperty("color").GetString().Should().Be("#ff3344");
    }

    [Fact]
    public async Task DigestPayloadIncludesAndMoreSuffix()
    {
        _server
            .Given(Request.Create().WithPath("/services/T/B/abc").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        SlackChannel channel = new(_httpClientFactory, NullLogger<SlackChannel>.Instance);
        List<Finding> findings = Enumerable
            .Range(0, 11)
            .Select(_ => NewFinding(Severity.High))
            .ToList();

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            findings,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Delivered.Should().Be(11);

        string body = _server.LogEntries.ToList()[0].RequestMessage.Body ?? "";
        using JsonDocument doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("blocks")[0]
            .GetProperty("text")
            .GetProperty("text")
            .GetString()
            .Should()
            .Contain("11 findings");
        doc.RootElement.GetProperty("blocks")[1]
            .GetProperty("text")
            .GetProperty("text")
            .GetString()
            .Should()
            .Contain("and 1 more");
        doc.RootElement.GetProperty("attachments")[0]
            .GetProperty("color")
            .GetString()
            .Should()
            .Be("#ff8800");
    }
}
