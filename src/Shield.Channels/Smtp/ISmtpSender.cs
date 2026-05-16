using System.Net.Mail;

namespace Shield.Channels.Smtp;

// Test seam: wraps SmtpClient so unit tests can intercept without binding a TCP server.
public interface ISmtpSender
{
    Task SendAsync(SmtpConfig config, MailMessage message, CancellationToken ct);
}

public sealed class SystemNetSmtpSender : ISmtpSender
{
    public async Task SendAsync(SmtpConfig config, MailMessage message, CancellationToken ct)
    {
        using SmtpClient client = new(config.Host, config.Port)
        {
            EnableSsl = config.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrWhiteSpace(config.Username))
        {
            client.Credentials = new System.Net.NetworkCredential(
                config.Username,
                config.Password ?? string.Empty
            );
        }
        await client.SendMailAsync(message, ct);
    }
}
