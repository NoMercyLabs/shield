using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.TargetId).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.RemoteIp).HasMaxLength(64);
        // Filter chips on the UI hit (Action, TargetType); list view orders by At desc.
        builder.HasIndex(entry => new
        {
            entry.At,
            entry.Action,
            entry.TargetType,
        });
    }
}
