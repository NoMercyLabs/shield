namespace Shield.Core.Domain;

public class AlertEvent
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public Guid ChannelId { get; set; }
    public DateTime SentAt { get; set; }
    public AlertStatus Status { get; set; }
    public string? Error { get; set; }
}
