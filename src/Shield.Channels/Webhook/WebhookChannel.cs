using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Webhook;

public sealed class WebhookChannel : IAlertChannel
{
    public const string HttpClientName = "webhook";
    private const int DigestThreshold = 5;
    private const string JsonContentType = "application/json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookChannel> _log;

    public WebhookChannel(IHttpClientFactory httpClientFactory, ILogger<WebhookChannel> log)
    {
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public ChannelType ChannelType => ChannelType.Webhook;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return AlertResult.Ok(0);

        WebhookConfig? config = JsonSerializer.Deserialize<WebhookConfig>(
            cfg.ConfigJsonEncrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config is null || !config.IsValid())
            return AlertResult.Fail("Webhook URL invalid or missing");

        bool digest = findings.Count >= DigestThreshold;
        HttpMethod method = ParseMethod(config.Method);

        string? overrideContentType = null;
        Dictionary<string, string> extraHeaders = new(StringComparer.OrdinalIgnoreCase);
        if (config.Headers is not null)
        {
            foreach (KeyValuePair<string, string> header in config.Headers)
            {
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    overrideContentType = header.Value;
                else
                    extraHeaders[header.Key] = header.Value;
            }
        }

        string body = !string.IsNullOrWhiteSpace(config.BodyTemplate)
            ? RenderTemplate(config.BodyTemplate!, findings, digest)
            : JsonSerializer.Serialize(
                digest ? new { count = findings.Count, findings } : (object)findings[0]
            );

        using StringContent content = new(
            body,
            Encoding.UTF8,
            overrideContentType ?? JsonContentType
        );
        using HttpRequestMessage request = new(method, config.Url) { Content = content };
        foreach (KeyValuePair<string, string> header in extraHeaders)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            HttpResponseMessage response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string respBody = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Webhook returned {Status}: {Body}", response.StatusCode, respBody);
                return AlertResult.Fail($"Webhook returned {(int)response.StatusCode}");
            }
            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Webhook POST failed");
            return AlertResult.Fail(ex.Message);
        }
    }

    // Anchor first finding as the template context; digest adds {{count}} ≥ 1.
    private static string RenderTemplate(
        string template,
        IReadOnlyList<Finding> findings,
        bool digest
    )
    {
        Finding anchor = findings[0];
        // Placeholders without a real data source resolve to empty so consumers can fail loudly server-side.
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["severity"] = anchor.Severity.ToString(),
            ["package"] = string.Empty,
            ["version"] = string.Empty,
            ["advisoryId"] = anchor.AdvisoryRefId.ToString(),
            ["summary"] = anchor.Notes ?? string.Empty,
            ["findingId"] = anchor.Id.ToString(),
            ["sourceName"] = anchor.SourceId.ToString(),
            ["count"] = (digest ? findings.Count : 1).ToString(),
        };

        StringBuilder sb = new(template.Length);
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

    private static HttpMethod ParseMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return HttpMethod.Post;
        return method.Trim().ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _ => new HttpMethod(method.Trim().ToUpperInvariant()),
        };
    }
}
