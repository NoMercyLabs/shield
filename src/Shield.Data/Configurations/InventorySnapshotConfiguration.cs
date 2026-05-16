using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class InventorySnapshotConfiguration : IEntityTypeConfiguration<InventorySnapshot>
{
    public void Configure(EntityTypeBuilder<InventorySnapshot> builder)
    {
        builder.ToTable("InventorySnapshots");
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.ContentsSha).IsRequired().HasMaxLength(128);

        builder
            .HasOne(snapshot => snapshot.Source)
            .WithMany()
            .HasForeignKey(snapshot => snapshot.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(snapshot => snapshot.SourceId);
    }
}
