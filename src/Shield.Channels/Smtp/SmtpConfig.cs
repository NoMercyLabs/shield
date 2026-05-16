namespace Shield.Channels.Smtp;

public sealed record SmtpConfig(
    string Host,
    int Port,
    bool UseStartTls,
    string? Username,
    string? Password,
    string From,
    string[] To,
    string? FromName = null,
    bool UseOAuth = false
)
{
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Host))
            return false;
        if (Port <= 0 || Port > 65535)
            return false;
        if (string.IsNullOrWhiteSpace(From))
            return false;
        if (To is null || To.Length == 0)
            return false;
        return To.All(addr => !string.IsNullOrWhiteSpace(addr));
    }
}
