using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Channels.Discord;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Alerter.Tests;

public class DiscordChannelTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceProvider _provider;

    public DiscordChannelTests()
    {
        _server = WireMockServer.Start();
        ServiceCollection services = [];
        services.AddHttpClient(DiscordWebhookChannel.HttpClientName);
        _provider = services.BuildServiceProvider();
        _httpClientFactory = _provider.GetRequiredService<IHttpClientFactory>();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _provider.Dispose();
    }

    private static Finding NewFinding(Severity severity) => new()
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

    private AlertChannel NewChannel() => new()
    {
        Id = Guid.NewGuid(),
        Type = ChannelType.Discord,
        Name = "discord",
        ConfigJsonEncrypted = JsonSerializer.Serialize(
            new { webhookUrl = $"{_server.Urls[0]}/webhooks/1/abc" }
        ),
        MinSeverity = Severity.Low,
        Enabled = true,
    };

    [Fact]
    public async Task SingleFindingPostsOneEmbedWithSeverityColor()
    {
        _server
            .Given(Request.Create().WithPath("/webhooks/1/abc").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        DiscordWebhookChannel channel = new(
            _httpClientFactory,
            NullLogger<DiscordWebhookChannel>.Instance
        );

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
        JsonElement embeds = doc.RootElement.GetProperty("embeds");
        embeds.GetArrayLength().Should().Be(1);
        JsonElement embed = embeds[0];
        embed.GetProperty("color").GetInt32().Should().Be(0xff3344);
        embed.GetProperty("title").GetString().Should().Contain("Critical");
    }

    [Fact]
    public async Task SixFindingsPostDigestWithAndOneMore()
    {
        _server
            .Given(Request.Create().WithPath("/webhooks/1/abc").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        DiscordWebhookChannel channel = new(
            _httpClientFactory,
            NullLogger<DiscordWebhookChannel>.Instance
        );

        // Inject 11 findings so digest preview (10) + "and 1 more" lands.
        List<Finding> findings = Enumerable.Range(0, 11)
            .Select(_ => NewFinding(Severity.High))
            .ToList();

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            findings,
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Delivered.Should().Be(11);

        var logs = _server.LogEntries.ToList();
        logs.Should().HaveCount(1);
        string body = logs[0].RequestMessage.Body ?? "";
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement embed = doc.RootElement.GetProperty("embeds")[0];
        embed.GetProperty("title").GetString().Should().Contain("11 findings");
        embed.GetProperty("description").GetString().Should().Contain("and 1 more");
        embed.GetProperty("color").GetInt32().Should().Be(0xff8800);
    }

    [Fact]
    public async Task InvalidWebhookUrlReturnsFailure()
    {
        DiscordWebhookChannel channel = new(
            _httpClientFactory,
            NullLogger<DiscordWebhookChannel>.Instance
        );

        AlertChannel cfg = new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Discord,
            Name = "discord",
            ConfigJsonEncrypted = JsonSerializer.Serialize(new { webhookUrl = "not-a-url" }),
            MinSeverity = Severity.Low,
            Enabled = true,
        };

        AlertResult result = await channel.SendAsync(
            cfg,
            [NewFinding(Severity.High)],
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("invalid");
    }

    [Fact]
    public async Task NonSuccessHttpReturnsFailure()
    {
        _server
            .Given(Request.Create().WithPath("/webhooks/1/abc").UsingPost())
            .RespondWith(Response.Create().WithStatusCode((int)HttpStatusCode.BadRequest)
                .WithBody("bad webhook"));

        DiscordWebhookChannel channel = new(
            _httpClientFactory,
            NullLogger<DiscordWebhookChannel>.Instance
        );

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [NewFinding(Severity.Low)],
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("400");
    }

    [Fact]
    public void DiscordConfigValidatesUrl()
    {
        new DiscordConfig("").IsValid().Should().BeFalse();
        new DiscordConfig("not-a-url").IsValid().Should().BeFalse();
        new DiscordConfig("ftp://x/y").IsValid().Should().BeFalse();
        new DiscordConfig("https://discord.com/api/webhooks/1/abc").IsValid().Should().BeTrue();
    }
}
