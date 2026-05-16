using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class SourceGroupConfiguration : IEntityTypeConfiguration<SourceGroup>
{
    public void Configure(EntityTypeBuilder<SourceGroup> builder)
    {
        builder.ToTable("SourceGroups");
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedOnAdd();
        builder.Property(group => group.Name).IsRequired().HasMaxLength(200);
        builder.Property(group => group.Description).HasMaxLength(2000);
    }
}
