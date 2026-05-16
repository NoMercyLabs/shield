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
    }
}
