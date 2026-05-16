namespace Shield.Core.Domain;

public class AgentToken
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool Revoked { get; set; }
}
