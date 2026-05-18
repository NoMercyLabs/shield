using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.ToTable("SavedFilters");
        builder.HasKey(filter => filter.Id);
        builder.Property(filter => filter.Name).HasMaxLength(200).IsRequired();
        builder.Property(filter => filter.Kind).HasMaxLength(40).IsRequired();
        builder.Property(filter => filter.QueryJson).IsRequired();
        // List-by-kind for the user is the only query; index supports both that and the
        // duplicate-name guard at the app layer (no DB unique — names can collide across
        // kinds for the same user).
        builder.HasIndex(filter => new
        {
            filter.UserId,
            filter.Kind,
            filter.CreatedAt,
        });
    }
}
