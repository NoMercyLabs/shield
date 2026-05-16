namespace Shield.Core.Domain;

public class AlertChannel
{
    public Guid Id { get; set; }
    public ChannelType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConfigJsonEncrypted { get; set; } = string.Empty;
    public Severity MinSeverity { get; set; }
    public bool Enabled { get; set; }
}
