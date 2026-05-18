using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Ntfy;

public sealed class NtfyChannel : IAlertChannel
{
    public const string HttpClientName = "ntfy";
    private const int DigestThreshold = 5;
    private const int DigestPreviewLimit = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NtfyChannel> _log;

    public NtfyChannel(IHttpClientFactory httpClientFactory, ILogger<NtfyChannel> log)
    {
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public ChannelType ChannelType => ChannelType.Ntfy;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return AlertResult.Ok(0);

        NtfyConfig? config = JsonSerializer.Deserialize<NtfyConfig>(
            cfg.ConfigJsonEncrypted,
            ChannelJson.Options
        );

        if (config is null || !config.IsValid())
            return AlertResult.Fail("Ntfy URL invalid or missing");

        bool digest = findings.Count >= DigestThreshold;
        Severity maxSeverity = findings.Max(finding => finding.Severity);
        // ntfy passes the title via HTTP header — keep it ASCII to avoid header encoding errors.
        string title = digest
            ? config.Title is { Length: > 0 } t
                ? AsciiOnly(t)
                : $"Shield digest - {findings.Count} findings"
            : config.Title is { Length: > 0 } t2
                ? AsciiOnly(t2)
                : $"Shield - {maxSeverity} finding";
        string body = digest ? BuildDigestBody(findings) : BuildSingleBody(findings[0]);
        int priority = config.Priority ?? PriorityFor(maxSeverity);
        string tags = config.Tags is { Length: > 0 }
            ? string.Join(",", config.Tags)
            : "shield,security";

        using StringContent content = new(body, Encoding.UTF8, "text/plain");
        using HttpRequestMessage request = new(HttpMethod.Post, config.Url) { Content = content };
        request.Headers.TryAddWithoutValidation("Title", title);
        request.Headers.TryAddWithoutValidation(
            "Priority",
            priority.ToString(CultureInfo.InvariantCulture)
        );
        request.Headers.TryAddWithoutValidation("Tags", tags);
        if (!string.IsNullOrWhiteSpace(config.AuthToken))
            request.Headers.Authorization = new("Bearer", config.AuthToken);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            HttpResponseMessage response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                string respBody = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Ntfy returned {Status}: {Body}", response.StatusCode, respBody);
                return AlertResult.Fail($"Ntfy returned {(int)response.StatusCode}");
            }
            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ntfy POST failed");
            return AlertResult.Fail(ex.Message);
        }
    }

    private static string BuildSingleBody(Finding finding) =>
        finding.Notes is { Length: > 0 } notes ? $"{notes}\n{finding.DedupKey}" : finding.DedupKey;

    private static string BuildDigestBody(IReadOnlyList<Finding> findings)
    {
        int previewCount = Math.Min(DigestPreviewLimit, findings.Count);
        int extra = findings.Count - previewCount;
        IEnumerable<string> lines = findings
            .Take(previewCount)
            .Select(finding => $"[{finding.Severity}] {finding.Notes ?? finding.DedupKey}");
        string body = string.Join("\n", lines);
        if (extra > 0)
            body += $"\nand {extra} more";
        return body;
    }

    private static int PriorityFor(Severity severity) =>
        severity switch
        {
            Severity.Critical => 5,
            Severity.High => 4,
            Severity.Medium => 3,
            Severity.Low => 2,
            _ => 3,
        };

    private static string AsciiOnly(string input)
    {
        StringBuilder sb = new(input.Length);
        foreach (char c in input)
            sb.Append(c <= 0x7F ? c : '?');
        return sb.ToString();
    }
}
