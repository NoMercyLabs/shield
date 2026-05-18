namespace Shield.Core.Domain;

// Named view of a Findings (or future Sources) filter selection. QueryJson is the
// URLSearchParams contents serialized as an object so the frontend can rehydrate it into
// the existing filter refs without server-side parsing.
public sealed class SavedFilter
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "findings";
    public string QueryJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
