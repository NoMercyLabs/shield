using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class IpReputationConfiguration : IEntityTypeConfiguration<IpReputation>
{
    public void Configure(EntityTypeBuilder<IpReputation> builder)
    {
        builder.ToTable("IpReputations");
        builder.HasKey(reputation => reputation.Id);
        builder.Property(reputation => reputation.Ip).HasMaxLength(64).IsRequired();
        builder.Property(reputation => reputation.LastJail).HasMaxLength(100);
        builder.Property(reputation => reputation.Notes).HasMaxLength(2000);
        builder.Property(reputation => reputation.Country).HasMaxLength(2);
        builder.HasIndex(reputation => reputation.Ip).IsUnique();
        // Reputation view default-sorts by LastSeenAt desc and filters banned/unbanned;
        // the composite lets both queries hit one covering index.
        builder.HasIndex(reputation => new { reputation.CurrentlyBanned, reputation.LastSeenAt });
    }
}
