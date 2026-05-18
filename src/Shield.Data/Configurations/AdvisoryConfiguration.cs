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

        // Unique key spans (Feed, ExternalId, Ecosystem, PackageName) because a single OSV /
        // GHSA vuln id fans out into one Advisory row per affected (package, ecosystem). The
        // narrower (Feed, ExternalId) shape rejects every fan-out advisory after the first.
        builder
            .HasIndex(advisory => new
            {
                advisory.Feed,
                advisory.ExternalId,
                advisory.Ecosystem,
                advisory.PackageName,
            })
            .IsUnique();

        builder.HasIndex(advisory => new { advisory.Ecosystem, advisory.PackageName });

        builder.Property(advisory => advisory.IsKev).HasDefaultValue(false);
        builder.Property(advisory => advisory.KevAddedAt);
        builder.Property(advisory => advisory.KevDueDate);
        builder.Property(advisory => advisory.EpssScore);
        builder.Property(advisory => advisory.EpssPercentile);
    }
}
