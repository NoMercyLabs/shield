using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Discord;

public sealed class DiscordWebhookChannel : IAlertChannel
{
    public const string HttpClientName = "discord";
    private const int DigestThreshold = 5;
    private const int DigestPreviewLimit = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiscordWebhookChannel> _log;

    public DiscordWebhookChannel(
        IHttpClientFactory httpClientFactory,
        ILogger<DiscordWebhookChannel> log
    )
    {
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public ChannelType ChannelType => ChannelType.Discord;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0) return AlertResult.Ok(0);

        // Encryption deferred — Phase 2: ConfigJsonEncrypted is plaintext today.
        DiscordConfig? config = JsonSerializer.Deserialize<DiscordConfig>(
            cfg.ConfigJsonEncrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config is null || !config.IsValid())
            return AlertResult.Fail("Discord webhook URL invalid or missing");

        object payload = findings.Count >= DigestThreshold
            ? BuildDigestPayload(findings)
            : BuildSinglePayload(findings[0]);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                config.WebhookUrl,
                payload,
                ct
            );

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning(
                    "Discord webhook returned {Status}: {Body}",
                    response.StatusCode,
                    body
                );
                return AlertResult.Fail($"Discord returned {(int)response.StatusCode}");
            }

            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord webhook POST failed");
            return AlertResult.Fail(ex.Message);
        }
    }

    private static object BuildSinglePayload(Finding finding) => new
    {
        embeds = new[]
        {
            new
            {
                title = $"Shield · {finding.Severity} finding",
                description = finding.Notes ?? $"Finding {finding.Id}",
                color = ColorFor(finding.Severity),
                fields = new[]
                {
                    new { name = "Severity", value = finding.Severity.ToString(), inline = true },
                    new { name = "State", value = finding.State.ToString(), inline = true },
                    new { name = "Dedup", value = finding.DedupKey, inline = false },
                },
                timestamp = finding.LastSeenAt.ToString("o"),
            },
        },
    };

    private static object BuildDigestPayload(IReadOnlyList<Finding> findings)
    {
        Severity max = findings.Max(finding => finding.Severity);
        int previewCount = Math.Min(DigestPreviewLimit, findings.Count);
        int extra = findings.Count - previewCount;

        IEnumerable<string> lines = findings
            .Take(previewCount)
            .Select(finding => $"• [{finding.Severity}] {finding.Notes ?? finding.DedupKey}");

        string description = string.Join("\n", lines);
        if (extra > 0) description += $"\nand {extra} more";

        return new
        {
            embeds = new[]
            {
                new
                {
                    title = $"Shield digest · {findings.Count} findings",
                    description,
                    color = ColorFor(max),
                    timestamp = DateTime.UtcNow.ToString("o"),
                },
            },
        };
    }

    private static int ColorFor(Severity severity) => severity switch
    {
        Severity.Critical => 0xff3344,
        Severity.High => 0xff8800,
        Severity.Medium => 0xffd633,
        Severity.Low => 0x66cc66,
        _ => 0x808080,
    };
}
