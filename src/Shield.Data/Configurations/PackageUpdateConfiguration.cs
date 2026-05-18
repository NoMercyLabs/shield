using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class PackageUpdateConfiguration : IEntityTypeConfiguration<PackageUpdate>
{
    public void Configure(EntityTypeBuilder<PackageUpdate> builder)
    {
        builder.ToTable("PackageUpdates");
        builder.HasKey(update => update.Id);
        builder.Property(update => update.Ecosystem).HasConversion<int>();
        builder.Property(update => update.Name).HasMaxLength(400).IsRequired();
        builder.Property(update => update.CurrentVersion).HasMaxLength(80).IsRequired();
        builder.Property(update => update.LatestVersion).HasMaxLength(80).IsRequired();
        builder.Property(update => update.AppliedPullRequestUrl).HasMaxLength(500);
        // Uniqueness — one row per (Source, Ecosystem, Name). UpdateScannerWorker upserts.
        builder
            .HasIndex(update => new
            {
                update.SourceId,
                update.Ecosystem,
                update.Name,
            })
            .IsUnique();
        // Browsing index — the SPA's /updates view paginates by source.
        builder.HasIndex(update => new { update.SourceId, update.AppliedAt });
    }
}
