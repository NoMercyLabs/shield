using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("Findings");
        builder.HasKey(finding => finding.Id);
        builder.Property(finding => finding.DedupKey).IsRequired().HasMaxLength(128);
        builder.Property(finding => finding.Notes).HasMaxLength(4000);

        builder.HasIndex(finding => finding.DedupKey).IsUnique();

        builder.HasIndex(finding => finding.SourceId);
        builder.HasIndex(finding => finding.InventoryItemId);
        builder.HasIndex(finding => finding.State);

        // Cascade on Source delete. Finding has no Source / InventoryItem navigation
        // property so EF can't infer the relationship from naming alone — without this
        // configuration the rows simply orphaned when a Source was deleted, which is the
        // bug source/112 surfaced. Configure only the SourceId path to avoid the
        // "multiple cascade paths" warning that would fire if we also wired up the
        // InventoryItemId cascade (Source → Snapshot → InventoryItem → Finding is the
        // same effect via a longer route, and the FK constraint isn't needed).
        builder
            .HasOne<Source>()
            .WithMany()
            .HasForeignKey(finding => finding.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
