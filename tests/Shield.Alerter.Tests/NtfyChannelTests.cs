using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Channels.Ntfy;
using Shield.Core.Domain;
using Shield.Core.Results;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Shield.Alerter.Tests;

public class NtfyChannelTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceProvider _provider;

    public NtfyChannelTests()
    {
        _server = WireMockServer.Start();
        ServiceCollection services = [];
        services.AddHttpClient(NtfyChannel.HttpClientName);
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

    private AlertChannel NewChannel(string? authToken = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Ntfy,
            Name = "ntfy",
            ConfigJsonEncrypted = JsonSerializer.Serialize(
                new { url = $"{_server.Urls[0]}/shield-alerts", authToken }
            ),
            MinSeverity = Severity.Low,
            Enabled = true,
        };

    [Fact]
    public async Task CriticalFindingSetsPriorityFiveAndTitleHeader()
    {
        _server
            .Given(Request.Create().WithPath("/shield-alerts").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        NtfyChannel channel = new(_httpClientFactory, NullLogger<NtfyChannel>.Instance);
        Finding finding = NewFinding(Severity.Critical);

        AlertResult result = await channel.SendAsync(
            NewChannel(),
            [finding],
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Error);
        result.Delivered.Should().Be(1);

        var logs = _server.LogEntries.ToList();
        logs.Should().HaveCount(1);
        var headers = logs[0].RequestMessage.Headers!;
        headers["Title"].Should().ContainSingle(h => h.Contains("Critical"));
        headers["Priority"].Should().ContainSingle(h => h == "5");
        headers["Tags"].Should().ContainSingle();
    }

    [Fact]
    public async Task AuthTokenAddsBearerHeader()
    {
        _server
            .Given(Request.Create().WithPath("/shield-alerts").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        NtfyChannel channel = new(_httpClientFactory, NullLogger<NtfyChannel>.Instance);

        AlertResult result = await channel.SendAsync(
            NewChannel(authToken: "tk_abc"),
            [NewFinding(Severity.Medium)],
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        var headers = _server.LogEntries.ToList()[0].RequestMessage.Headers!;
        headers["Authorization"].Should().ContainSingle(h => h == "Bearer tk_abc");
    }

    [Fact]
    public async Task InvalidUrlReturnsFailure()
    {
        NtfyChannel channel = new(_httpClientFactory, NullLogger<NtfyChannel>.Instance);
        AlertChannel cfg = new()
        {
            Id = Guid.NewGuid(),
            Type = ChannelType.Ntfy,
            Name = "ntfy",
            ConfigJsonEncrypted = JsonSerializer.Serialize(new { url = "not-a-url" }),
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
}
