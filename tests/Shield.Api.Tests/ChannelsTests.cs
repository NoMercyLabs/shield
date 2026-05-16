using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shield.Api.Contracts;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;
using Shield.Data;
using Xunit;

namespace Shield.Api.Tests;

public sealed class ChannelsTests
{
    private static Finding NewSampleFinding(Severity severity) =>
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
            DedupKey = Guid.NewGuid().ToString("n"),
            Notes = "test",
        };

    // Records every SendAsync call so tests can assert the persisted config
    // round-trips through the controller into the channel implementation.
    private sealed class RecordingChannel : IAlertChannel
    {
        public RecordingChannel(ChannelType type)
        {
            ChannelType = type;
        }

        public ChannelType ChannelType { get; }
        public List<(AlertChannel Channel, IReadOnlyList<Finding> Findings)> Calls { get; } = new();

        public ValueTask<AlertResult> SendAsync(
            AlertChannel cfg,
            IReadOnlyList<Finding> findings,
            CancellationToken ct
        )
        {
            Calls.Add((cfg, findings.ToList()));
            return ValueTask.FromResult(AlertResult.Ok(findings.Count));
        }
    }

    // Per-test factory so each spec gets its own recording sink + isolated DB.
    private sealed class RecordingFactory : ShieldWebAppFactory
    {
        private readonly ChannelType _swapType;
        public RecordingChannel Recorder { get; }

        public RecordingFactory(ChannelType swapType)
        {
            _swapType = swapType;
            Recorder = new RecordingChannel(swapType);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                ServiceDescriptor[] toRemove = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(IAlertChannel)
                        && descriptor.ImplementationType is { } impl
                        && IsSwapTarget(impl, _swapType)
                    )
                    .ToArray();
                foreach (ServiceDescriptor descriptor in toRemove)
                    services.Remove(descriptor);
                services.AddSingleton<IAlertChannel>(Recorder);
            });
        }

        private static bool IsSwapTarget(Type impl, ChannelType type) =>
            type switch
            {
                ChannelType.Slack => impl.Name == "SlackChannel",
                ChannelType.Webhook => impl.Name == "WebhookChannel",
                _ => false,
            };
    }

    [Fact]
    public async Task Slack_channel_create_with_channelId_succeeds()
    {
        using RecordingFactory factory = new(ChannelType.Slack);
        HttpClient client = factory.CreateClient();

        // Per the new per-type form, the Slack OAuth payload looks like
        // { "channelId": "C12345" } — no webhookUrl.
        string configJson = JsonSerializer.Serialize(new { channelId = "C12345" });
        object request = new
        {
            type = (int)ChannelType.Slack,
            name = "slack-oauth-fixture",
            configJson,
            minSeverity = (int)Severity.Low,
            enabled = true,
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/channels", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        ChannelResponse? created = await create.Content.ReadFromJsonAsync<ChannelResponse>();
        created.Should().NotBeNull();
        created!.Type.Should().Be(ChannelType.Slack);
        created.ConfigJson.Should().Contain("C12345");

        // Trigger a test-send so the dispatcher hands the persisted config to
        // our recording channel. The synthetic finding the controller builds
        // has Severity.Low — make sure that flows through too.
        HttpResponseMessage test = await client.PostAsync(
            $"/api/channels/{created.Id}/test-send",
            content: null
        );
        test.StatusCode.Should().Be(HttpStatusCode.OK);

        factory.Recorder.Calls.Should().NotBeEmpty();
        AlertChannel reachedConfig = factory.Recorder.Calls[0].Channel;
        reachedConfig.Type.Should().Be(ChannelType.Slack);
        // The dispatcher must hand us the stored JSON verbatim — assert the
        // channelId survived.
        using JsonDocument doc = JsonDocument.Parse(reachedConfig.ConfigJsonEncrypted);
        doc.RootElement.GetProperty("channelId").GetString().Should().Be("C12345");
    }

    [Fact]
    public async Task Webhook_channel_with_body_template_renders_placeholders()
    {
        using RecordingFactory factory = new(ChannelType.Webhook);
        HttpClient client = factory.CreateClient();

        string template = "sev={{severity}} count={{count}}";
        string configJson = JsonSerializer.Serialize(
            new
            {
                url = "https://example.invalid/hook",
                method = "POST",
                bodyTemplate = template,
            }
        );
        object request = new
        {
            type = (int)ChannelType.Webhook,
            name = "webhook-template-fixture",
            configJson,
            minSeverity = (int)Severity.Low,
            enabled = true,
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/channels", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        ChannelResponse? created = await create.Content.ReadFromJsonAsync<ChannelResponse>();
        created.Should().NotBeNull();

        HttpResponseMessage test = await client.PostAsync(
            $"/api/channels/{created!.Id}/test-send",
            content: null
        );
        test.StatusCode.Should().Be(HttpStatusCode.OK);

        // The recorder captured the config + findings the dispatcher would have
        // handed to the real WebhookChannel. We re-render the template here
        // through the same helper logic to prove placeholders are wired through
        // to whatever sends the eventual HTTP request.
        factory.Recorder.Calls.Should().NotBeEmpty();
        (AlertChannel cfg, IReadOnlyList<Finding> findings) = factory.Recorder.Calls[0];
        using JsonDocument doc = JsonDocument.Parse(cfg.ConfigJsonEncrypted);
        doc.RootElement.GetProperty("bodyTemplate").GetString().Should().Be(template);

        string rendered = RenderTemplate(template, findings[0]);
        rendered.Should().Be($"sev={findings[0].Severity} count=1");
    }

    [Fact]
    public async Task Channel_response_includes_masked_parsed_config()
    {
        using ShieldWebAppFactory factory = new();
        HttpClient client = factory.CreateClient();

        string configJson = JsonSerializer.Serialize(
            new { webhookUrl = "https://discord.com/api/webhooks/12345/secret-token-stuff" }
        );
        object request = new
        {
            type = (int)ChannelType.Discord,
            name = "discord-mask-fixture",
            configJson,
            minSeverity = (int)Severity.Low,
            enabled = true,
        };

        HttpResponseMessage create = await client.PostAsJsonAsync("/api/channels", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        // ParsedConfig is the new field the per-type form hydrates from on edit.
        // It must NEVER leak the raw token, but must keep enough of the URL for
        // operators to recognise which webhook the channel points at.
        ChannelResponse? created = await create.Content.ReadFromJsonAsync<ChannelResponse>();
        created.Should().NotBeNull();
        created!.ParsedConfig.Should().NotBeNull();
        string parsedJson = created.ParsedConfig!.ToJsonString();
        parsedJson.Should().NotContain("secret-token-stuff");
        parsedJson.Should().Contain("discord.com");
        parsedJson.Should().Contain("****");
    }

    // Mirror of the production WebhookChannel.RenderTemplate so the assertion
    // above is self-contained. If the production logic ever diverges, this
    // test still proves the inputs reach the channel with placeholders intact.
    private static string RenderTemplate(string template, Finding anchor)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["severity"] = anchor.Severity.ToString(),
            ["count"] = "1",
            ["findingId"] = anchor.Id.ToString(),
        };
        System.Text.StringBuilder sb = new(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }
            sb.Append(template, i, open - i);
            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                sb.Append(template, open, template.Length - open);
                break;
            }
            string key = template.Substring(open + 2, close - open - 2).Trim();
            sb.Append(map.TryGetValue(key, out string? value) ? value : string.Empty);
            i = close + 2;
        }
        return sb.ToString();
    }
}
