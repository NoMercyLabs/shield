using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("Sources");
        builder.HasKey(source => source.Id);
        builder.Property(source => source.Id).ValueGeneratedOnAdd();
        builder.Property(source => source.Name).IsRequired().HasMaxLength(200);
        builder.Property(source => source.ConfigJson).IsRequired();
        builder.Property(source => source.LastError).HasMaxLength(2000);
        builder.Property(source => source.DetectedRemote).HasMaxLength(2000);
    }
}
