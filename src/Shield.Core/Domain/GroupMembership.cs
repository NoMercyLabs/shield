namespace Shield.Core.Domain;

public sealed class GroupMembership
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AddedAt { get; set; }
}
