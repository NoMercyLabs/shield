using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Shield.Channels.Smtp;

namespace Shield.Api.Services.Notifications;

public sealed class InviteEmailSender : IInviteEmailSender
{
    private readonly ShieldDbContext _db;
    private readonly ISmtpSender _smtpSender;
    private readonly ILogger<InviteEmailSender> _log;

    public InviteEmailSender(
        ShieldDbContext db,
        ISmtpSender smtpSender,
        ILogger<InviteEmailSender> log
    )
    {
        _db = db;
        _smtpSender = smtpSender;
        _log = log;
    }

    public async Task<InviteEmailResult> SendAsync(
        Invite invite,
        string acceptUrl,
        string inviterLogin,
        IReadOnlyList<string> sourceGroupNames,
        CancellationToken ct
    )
    {
        AlertChannel? smtpChannel = await _db
            .AlertChannels.AsNoTracking()
            .Where(channel => channel.Type == ChannelType.Smtp && channel.Enabled)
            .OrderBy(channel => channel.Name)
            .FirstOrDefaultAsync(ct);

        if (smtpChannel is null)
        {
            _log.LogWarning(
                "Invite {InviteId}: no enabled SMTP channel — email skipped. Operator must relay the accept URL manually.",
                invite.Id
            );
            return new(false, "no_smtp_channel");
        }

        SmtpConfig? config = JsonSerializer.Deserialize<SmtpConfig>(
            smtpChannel.ConfigJsonEncrypted,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        if (config is null || !config.IsValid())
        {
            _log.LogWarning(
                "Invite {InviteId}: SMTP channel {ChannelId} config invalid — email skipped.",
                invite.Id,
                smtpChannel.Id
            );
            return new(false, "smtp_config_invalid");
        }
        if (config.UseOAuth)
        {
            // XOAUTH2 isn't supported by the BCL SmtpClient — fall back to "log only" so the
            // invite still lands and the admin can relay manually. Mirrors the SmtpChannel
            // alert-path behaviour in Shield.Channels/Smtp/SmtpChannel.cs.
            _log.LogWarning(
                "Invite {InviteId}: SMTP channel uses OAuth which is not yet supported for invite email.",
                invite.Id
            );
            return new(false, "smtp_oauth_unsupported");
        }

        string plain = BuildPlainBody(invite, acceptUrl, inviterLogin, sourceGroupNames);
        string html = BuildHtmlBody(invite, acceptUrl, inviterLogin, sourceGroupNames);
        string subject = $"You've been invited to Shield by {inviterLogin}";

        using MailMessage message = new()
        {
            From = string.IsNullOrWhiteSpace(config.FromName)
                ? new(config.From)
                : new MailAddress(config.From, config.FromName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = plain,
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
        };
        message.To.Add(invite.Email);

        // Layer the HTML view as an alternate so clients that prefer rich content pick it up,
        // while plain-text-only readers still get the readable body above.
        AlternateView htmlView = AlternateView.CreateAlternateViewFromString(
            html,
            Encoding.UTF8,
            "text/html"
        );
        message.AlternateViews.Add(htmlView);

        try
        {
            await _smtpSender.SendAsync(config, message, ct);
            return new(true, null);
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Invite {InviteId}: SMTP send failed via channel {ChannelId}",
                invite.Id,
                smtpChannel.Id
            );
            return new(false, "smtp_send_failed");
        }
    }

    private static string BuildPlainBody(
        Invite invite,
        string acceptUrl,
        string inviterLogin,
        IReadOnlyList<string> sourceGroupNames
    )
    {
        StringBuilder sb = new();
        sb.AppendLine($"You've been invited to Shield by {inviterLogin}.");
        sb.AppendLine();
        sb.AppendLine(
            "Shield is a self-hosted dependency vulnerability watcher. "
                + $"{inviterLogin} has granted you {invite.Role} access to "
                + $"{sourceGroupNames.Count} source group{(sourceGroupNames.Count == 1 ? "" : "s")}."
        );
        if (sourceGroupNames.Count > 0)
        {
            sb.AppendLine();
            foreach (string name in sourceGroupNames)
                sb.AppendLine($"  - {name}");
        }
        sb.AppendLine();
        sb.AppendLine($"Accept the invite (link expires {invite.ExpiresAt:yyyy-MM-dd HH:mm} UTC):");
        sb.AppendLine(acceptUrl);
        sb.AppendLine();
        sb.AppendLine("If you weren't expecting this, ignore the email.");
        return sb.ToString();
    }

    private static string BuildHtmlBody(
        Invite invite,
        string acceptUrl,
        string inviterLogin,
        IReadOnlyList<string> sourceGroupNames
    )
    {
        StringBuilder sb = new();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;line-height:1.5\">");
        sb.Append("<p>You've been invited to Shield by <strong>");
        sb.Append(HtmlEncoder.Default.Encode(inviterLogin));
        sb.Append("</strong>.</p>");

        sb.Append(
            "<p>Shield is a self-hosted dependency vulnerability watcher. "
                + HtmlEncoder.Default.Encode(inviterLogin)
                + " has granted you <strong>"
                + HtmlEncoder.Default.Encode(invite.Role)
                + "</strong> access to "
                + sourceGroupNames.Count
                + " source group"
                + (sourceGroupNames.Count == 1 ? "" : "s")
                + ".</p>"
        );

        if (sourceGroupNames.Count > 0)
        {
            sb.Append("<ul>");
            foreach (string name in sourceGroupNames)
            {
                sb.Append("<li>");
                sb.Append(HtmlEncoder.Default.Encode(name));
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("<p><a href=\"");
        sb.Append(HtmlEncoder.Default.Encode(acceptUrl));
        sb.Append(
            "\" style=\"display:inline-block;padding:10px 16px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px\">Accept invitation</a></p>"
        );
        sb.Append("<p style=\"color:#64748b;font-size:13px\">Link expires ");
        sb.Append(invite.ExpiresAt.ToString("yyyy-MM-dd HH:mm"));
        sb.Append(" UTC. If you weren't expecting this, ignore the email.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
