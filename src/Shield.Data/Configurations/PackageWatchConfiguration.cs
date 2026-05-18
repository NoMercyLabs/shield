using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class PackageWatchConfiguration : IEntityTypeConfiguration<PackageWatch>
{
    public void Configure(EntityTypeBuilder<PackageWatch> builder)
    {
        builder.ToTable("PackageWatches");
        builder.HasKey(watch => watch.Id);
        builder.Property(watch => watch.Ecosystem).HasConversion<int>();
        builder.Property(watch => watch.PackageName).HasMaxLength(400).IsRequired();
        // Star uniqueness — one row per (operator, ecosystem, package). Case-sensitive
        // since SQLite default collation is BINARY and ecosystems differ on case rules.
        builder
            .HasIndex(watch => new
            {
                watch.UserId,
                watch.Ecosystem,
                watch.PackageName,
            })
            .IsUnique();
    }
}
