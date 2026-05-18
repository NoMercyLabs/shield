namespace Shield.Core.Domain;

// Outstanding owner-issued invite. A row lives from "send email" → "accept" (or "revoke" /
// "expire"). The ShieldUser is NOT created up-front: that would let anyone holding the token
// probe whether an email is already registered. CreatedBy + AcceptedAt are the audit handles.
public sealed class Invite
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // Comma-separated SourceGroup ids the invitee will be added to on accept. Stored as a
    // string (not a join table) because the invite is short-lived and a join would mean an
    // extra cascade dance on revoke/expire — keep the schema simple, expand later if needed.
    public string SourceGroupIdsCsv { get; set; } = string.Empty;

    // The shared secret in the email link. Indexed UNIQUE; never echoed to logs.
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedBy { get; set; }
    public int ResendCount { get; set; }
    public DateTime? LastSentAt { get; set; }

    // Pre-bound external identity (currently always GitHub). When non-null, the invite is
    // tied to a specific provider account: the OAuth subject id observed at accept-time must
    // match PreBoundSubjectId, otherwise the wrong person clicking the link can't claim it.
    // PreBoundLogin / PreBoundEmail are stored for display only (audit + outbound email).
    // Email-based invites leave all four NULL — the legacy path.
    // NOTE: InviteAcceptanceController must verify acceptanceTicket.SubjectId == PreBoundSubjectId
    // when PreBoundProvider is set. The auth-state agent owns the consumer-side check.
    public string? PreBoundProvider { get; set; }
    public string? PreBoundSubjectId { get; set; }
    public string? PreBoundLogin { get; set; }
    public string? PreBoundEmail { get; set; }
}
