using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class FeedSyncStateConfiguration : IEntityTypeConfiguration<FeedSyncState>
{
    public void Configure(EntityTypeBuilder<FeedSyncState> builder)
    {
        builder.ToTable("FeedSyncStates");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.LastError).HasMaxLength(2000);
        builder.Property(state => state.Cursor).HasMaxLength(400);

        builder
            .HasIndex(state => state.Feed)
            .IsUnique();
    }
}
