using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class AdvisoryConfiguration : IEntityTypeConfiguration<Advisory>
{
    public void Configure(EntityTypeBuilder<Advisory> builder)
    {
        builder.ToTable("Advisories");
        builder.HasKey(advisory => advisory.Id);
        builder.Property(advisory => advisory.ExternalId).IsRequired().HasMaxLength(200);
        builder.Property(advisory => advisory.PackageName).IsRequired().HasMaxLength(400);
        builder.Property(advisory => advisory.AffectedRangesJson).IsRequired();
        builder.Property(advisory => advisory.Summary).HasMaxLength(4000);
        builder.Property(advisory => advisory.ReferencesJson).IsRequired();

        builder.HasIndex(advisory => new { advisory.Feed, advisory.ExternalId }).IsUnique();

        builder.HasIndex(advisory => new { advisory.Ecosystem, advisory.PackageName });
    }
}
