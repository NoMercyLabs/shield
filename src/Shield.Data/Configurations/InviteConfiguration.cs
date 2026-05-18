using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public sealed class InviteConfiguration : IEntityTypeConfiguration<Invite>
{
    public void Configure(EntityTypeBuilder<Invite> builder)
    {
        builder.ToTable("Invites");
        builder.HasKey(invite => invite.Id);
        builder.Property(invite => invite.Email).HasMaxLength(320).IsRequired();
        builder.Property(invite => invite.Role).HasMaxLength(64).IsRequired();
        builder.Property(invite => invite.SourceGroupIdsCsv).HasMaxLength(1024).IsRequired();
        builder.Property(invite => invite.Token).HasMaxLength(128).IsRequired();
        builder.Property(invite => invite.PreBoundProvider).HasMaxLength(32);
        builder.Property(invite => invite.PreBoundSubjectId).HasMaxLength(128);
        builder.Property(invite => invite.PreBoundLogin).HasMaxLength(128);
        builder.Property(invite => invite.PreBoundEmail).HasMaxLength(320);
        // UNIQUE on token so a brute-force collision on the random secret is a constraint
        // error, not a silent overwrite. The /api/access/invite/{token} lookup is index-driven.
        builder.HasIndex(invite => invite.Token).IsUnique();
        // Lookups in the pending-invites list scan by AcceptedAt/RevokedAt — cover the common case.
        builder.HasIndex(invite => new
        {
            invite.AcceptedAt,
            invite.RevokedAt,
            invite.ExpiresAt,
        });
    }
}
