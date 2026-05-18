using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedOnAdd();
        builder.Property(item => item.Name).IsRequired().HasMaxLength(400);
        builder.Property(item => item.Version).IsRequired().HasMaxLength(200);
        builder.Property(item => item.ParentChain).IsRequired();
        builder.Property(item => item.ManifestPath).HasMaxLength(1000);

        builder
            .HasOne(item => item.Snapshot)
            .WithMany(snapshot => snapshot.Items)
            .HasForeignKey(item => item.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => new
        {
            item.SnapshotId,
            item.Ecosystem,
            item.Name,
            item.Version,
        });
    }
}
