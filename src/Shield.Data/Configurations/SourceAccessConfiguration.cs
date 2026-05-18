using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class SourceAccessConfiguration : IEntityTypeConfiguration<SourceAccess>
{
    public void Configure(EntityTypeBuilder<SourceAccess> builder)
    {
        builder.ToTable("SourceAccesses");
        builder.HasKey(access => access.Id);
        builder.Property(access => access.Id).ValueGeneratedOnAdd();
        builder.Property(access => access.Level).HasConversion<int>();
        // Resolver hot paths: visible-ids by user (UserId IN grants) and group lookup
        // (GroupId IN user's groups). Composite indexes on (SourceId, ...) also satisfy
        // the per-source admin views that list grants for one source.
        builder.HasIndex(access => new { access.SourceId, access.UserId });
        builder.HasIndex(access => new { access.SourceId, access.GroupId });

        // Cascade ACL rows when the parent source goes away — without this the rows hung
        // around as ghost grants pointing at non-existent sources.
        builder
            .HasOne<Source>()
            .WithMany()
            .HasForeignKey(access => access.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
