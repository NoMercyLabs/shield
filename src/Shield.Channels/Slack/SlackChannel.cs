using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Slack;

public sealed class SlackChannel : IAlertChannel
{
    public const string HttpClientName = "slack";
    public const string ChatPostMessageUrl = "https://slack.com/api/chat.postMessage";
    private const int DigestThreshold = 5;
    private const int DigestPreviewLimit = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SlackChannel> _log;
    private readonly IOAuthTokenAccessor? _tokenAccessor;

    public SlackChannel(
        IHttpClientFactory httpClientFactory,
        ILogger<SlackChannel> log,
        IOAuthTokenAccessor? tokenAccessor = null
    )
    {
        _httpClientFactory = httpClientFactory;
        _log = log;
        _tokenAccessor = tokenAccessor;
    }

    public ChannelType ChannelType => ChannelType.Slack;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return AlertResult.Ok(0);

        SlackConfig? config = JsonSerializer.Deserialize<SlackConfig>(
            cfg.ConfigJsonEncrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config is null || !config.IsValid())
            return AlertResult.Fail("Slack config invalid or missing");

        return config.UsesOAuth
            ? await SendViaOAuthAsync(config, findings, ct)
            : await SendViaWebhookAsync(config, findings, ct);
    }

    private async Task<AlertResult> SendViaWebhookAsync(
        SlackConfig config,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        object payload =
            findings.Count >= DigestThreshold
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
                    "Slack webhook returned {Status}: {Body}",
                    response.StatusCode,
                    body
                );
                return AlertResult.Fail($"Slack returned {(int)response.StatusCode}");
            }
            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Slack webhook POST failed");
            return AlertResult.Fail(ex.Message);
        }
    }

    private async Task<AlertResult> SendViaOAuthAsync(
        SlackConfig config,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (_tokenAccessor is null)
            return AlertResult.Fail("Slack OAuth not wired (token accessor missing)");

        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Slack, ct);
        if (string.IsNullOrEmpty(token))
            return AlertResult.Fail("Slack not connected — connect a workspace in Settings");

        Dictionary<string, object?> payload =
            findings.Count >= DigestThreshold
                ? (Dictionary<string, object?>)BuildOAuthDigestPayload(findings, config.ChannelId!)
                : (Dictionary<string, object?>)
                    BuildOAuthSinglePayload(findings[0], config.ChannelId!);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, ChatPostMessageUrl)
            {
                Content = JsonContent.Create(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using HttpResponseMessage response = await client.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "Slack chat.postMessage returned {Status}: {Body}",
                    response.StatusCode,
                    body
                );
                return AlertResult.Fail($"Slack returned {(int)response.StatusCode}");
            }
            // chat.postMessage always returns 200 — check the ok field.
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("ok", out JsonElement okEl) && !okEl.GetBoolean())
            {
                string err = doc.RootElement.TryGetProperty("error", out JsonElement e)
                    ? e.GetString() ?? "unknown"
                    : "unknown";
                _log.LogWarning("Slack chat.postMessage api error: {Error}", err);
                return AlertResult.Fail($"Slack error: {err}");
            }
            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Slack chat.postMessage POST failed");
            return AlertResult.Fail(ex.Message);
        }
    }

    private static Dictionary<string, object?> BuildOAuthSinglePayload(
        Finding finding,
        string channelId
    )
    {
        Dictionary<string, object?> wrapped = new(BuildSinglePayloadDict(finding))
        {
            ["channel"] = channelId,
        };
        return wrapped;
    }

    private static Dictionary<string, object?> BuildOAuthDigestPayload(
        IReadOnlyList<Finding> findings,
        string channelId
    )
    {
        Dictionary<string, object?> wrapped = new(BuildDigestPayloadDict(findings))
        {
            ["channel"] = channelId,
        };
        return wrapped;
    }

    private static Dictionary<string, object?> BuildSinglePayloadDict(Finding finding)
    {
        object payload = BuildSinglePayload(finding);
        // Serialize and back to dict so we can add `channel`.
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(payload)
        )!;
    }

    private static Dictionary<string, object?> BuildDigestPayloadDict(
        IReadOnlyList<Finding> findings
    )
    {
        object payload = BuildDigestPayload(findings);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(payload)
        )!;
    }

    private static object BuildSinglePayload(Finding finding) =>
        new
        {
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = $"Shield · {finding.Severity} finding",
                    },
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Severity*\n{finding.Severity}" },
                        new { type = "mrkdwn", text = $"*State*\n{finding.State}" },
                        new { type = "mrkdwn", text = $"*Dedup*\n`{finding.DedupKey}`" },
                    },
                },
            },
            attachments = new object[]
            {
                new
                {
                    color = ColorFor(finding.Severity),
                    text = finding.Notes ?? $"Finding {finding.Id}",
                    ts = new DateTimeOffset(finding.LastSeenAt, TimeSpan.Zero).ToUnixTimeSeconds(),
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
            .Select(finding => $"• [{finding.Severity}] `{finding.DedupKey}`");
        string text = string.Join("\n", lines);
        if (extra > 0)
            text += $"\nand {extra} more";

        return new
        {
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = $"Shield digest · {findings.Count} findings",
                    },
                },
                new { type = "section", text = new { type = "mrkdwn", text } },
            },
            attachments = new object[]
            {
                new { color = ColorFor(max), text = $"Highest severity: {max}" },
            },
        };
    }

    private static string ColorFor(Severity severity) =>
        severity switch
        {
            Severity.Critical => "#ff3344",
            Severity.High => "#ff8800",
            Severity.Medium => "#ffd633",
            Severity.Low => "#66cc66",
            _ => "#808080",
        };
}
