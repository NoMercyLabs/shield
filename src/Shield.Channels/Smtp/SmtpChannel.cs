using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Core.Results;

namespace Shield.Channels.Smtp;

public sealed class SmtpChannel : IAlertChannel
{
    private const int DigestThreshold = 5;
    private const int DigestPreviewLimit = 20;

    private readonly ISmtpSender _sender;
    private readonly ILogger<SmtpChannel> _log;
    private readonly IOAuthTokenAccessor? _tokenAccessor;

    public SmtpChannel(
        ISmtpSender sender,
        ILogger<SmtpChannel> log,
        IOAuthTokenAccessor? tokenAccessor = null
    )
    {
        _sender = sender;
        _log = log;
        _tokenAccessor = tokenAccessor;
    }

    public ChannelType ChannelType => ChannelType.Smtp;

    public async ValueTask<AlertResult> SendAsync(
        AlertChannel cfg,
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return AlertResult.Ok(0);

        SmtpConfig? config = JsonSerializer.Deserialize<SmtpConfig>(
            cfg.ConfigJsonEncrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (config is null || !config.IsValid())
            return AlertResult.Fail("SMTP config invalid or missing");

        // XOAUTH2 requires SASL hookup not available on System.Net.Mail.SmtpClient.
        // We validate the user has connected Google so the channel form passes, then
        // fail the actual send with a clear message until a MailKit migration lands.
        if (config.UseOAuth)
        {
            if (_tokenAccessor is null)
                return AlertResult.Fail("SMTP OAuth requested but token accessor is missing");
            string? googleToken = await _tokenAccessor.GetAccessTokenAsync(
                OAuthProvider.Google,
                ct
            );
            if (string.IsNullOrEmpty(googleToken))
                return AlertResult.Fail("Google account not connected — connect in Settings");
            _log.LogWarning(
                "SMTP XOAUTH2 not yet implemented in .NET BCL SmtpClient — token is fresh, but send is blocked until MailKit lands"
            );
            return AlertResult.Fail(
                "SMTP XOAUTH2 send is not yet implemented (token stored, waiting on MailKit migration)"
            );
        }

        bool digest = findings.Count >= DigestThreshold;
        Severity maxSeverity = findings.Max(finding => finding.Severity);
        string subject = digest
            ? $"[Shield · {maxSeverity}] digest · {findings.Count} findings"
            : $"[Shield · {findings[0].Severity}] {findings[0].DedupKey}";
        string html = digest ? BuildDigestHtml(findings) : BuildSingleHtml(findings[0]);

        using MailMessage message = new()
        {
            From = string.IsNullOrWhiteSpace(config.FromName)
                ? new(config.From)
                : new MailAddress(config.From, config.FromName),
            Subject = subject,
            Body = html,
            IsBodyHtml = true,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
        };
        foreach (string recipient in config.To)
            message.To.Add(recipient);

        try
        {
            await _sender.SendAsync(config, message, ct);
            return AlertResult.Ok(findings.Count);
        }
        catch (Exception ex)
        {
            // Never let the password surface in log/error text.
            string redacted = Redact(ex.Message, config.Password);
            _log.LogError("SMTP send failed: {Reason}", redacted);
            return AlertResult.Fail(redacted);
        }
    }

    private static string BuildSingleHtml(Finding finding)
    {
        StringBuilder sb = new();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif\">");
        sb.Append($"<h2 style=\"color:{HtmlColorFor(finding.Severity)}\">Shield · ");
        sb.Append(HtmlEncoder.Default.Encode(finding.Severity.ToString()));
        sb.Append(" finding</h2>");
        sb.Append("<table style=\"border-collapse:collapse\" cellpadding=\"6\">");
        AppendRow(sb, "Severity", finding.Severity.ToString());
        AppendRow(sb, "State", finding.State.ToString());
        AppendRow(sb, "Dedup", finding.DedupKey);
        AppendRow(sb, "First seen", finding.FirstSeenAt.ToString("u"));
        AppendRow(sb, "Last seen", finding.LastSeenAt.ToString("u"));
        if (!string.IsNullOrWhiteSpace(finding.Notes))
            AppendRow(sb, "Notes", finding.Notes!);
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string BuildDigestHtml(IReadOnlyList<Finding> findings)
    {
        Severity max = findings.Max(finding => finding.Severity);
        int previewCount = Math.Min(DigestPreviewLimit, findings.Count);
        int extra = findings.Count - previewCount;

        StringBuilder sb = new();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif\">");
        sb.Append(
            $"<h2 style=\"color:{HtmlColorFor(max)}\">Shield digest · {findings.Count} findings</h2>"
        );
        sb.Append("<ul>");
        foreach (Finding finding in findings.Take(previewCount))
        {
            sb.Append("<li>[");
            sb.Append(HtmlEncoder.Default.Encode(finding.Severity.ToString()));
            sb.Append("] ");
            string summary = finding.Notes ?? finding.DedupKey;
            sb.Append(HtmlEncoder.Default.Encode(summary));
            sb.Append("</li>");
        }
        sb.Append("</ul>");
        if (extra > 0)
            sb.Append($"<p>and {extra} more</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, string value)
    {
        sb.Append("<tr><td style=\"font-weight:bold\">");
        sb.Append(HtmlEncoder.Default.Encode(label));
        sb.Append("</td><td>");
        sb.Append(HtmlEncoder.Default.Encode(value));
        sb.Append("</td></tr>");
    }

    private static string HtmlColorFor(Severity severity) =>
        severity switch
        {
            Severity.Critical => "#ff3344",
            Severity.High => "#ff8800",
            Severity.Medium => "#ffd633",
            Severity.Low => "#66cc66",
            _ => "#808080",
        };

    private static string Redact(string message, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return message;
        return message.Replace(secret, "***", StringComparison.Ordinal);
    }
}
