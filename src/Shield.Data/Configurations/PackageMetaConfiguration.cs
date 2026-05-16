using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class PackageMetaConfiguration : IEntityTypeConfiguration<PackageMeta>
{
    public void Configure(EntityTypeBuilder<PackageMeta> builder)
    {
        builder.ToTable("PackageMetas");
        builder.HasKey(meta => meta.Id);
        builder.Property(meta => meta.Name).IsRequired().HasMaxLength(400);
        builder.Property(meta => meta.Version).IsRequired().HasMaxLength(200);
        builder.Property(meta => meta.MaintainersJson).IsRequired();
        builder.Property(meta => meta.TarballSha).HasMaxLength(128);

        builder
            .HasIndex(meta => new { meta.Ecosystem, meta.Name, meta.Version })
            .IsUnique();
    }
}
