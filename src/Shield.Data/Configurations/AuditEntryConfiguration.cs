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
        // BeforeJson / AfterJson kept as plain TEXT — typical payloads are < 4 KB and SQLite
        // has no TEXT-length penalty. No index: query patterns are PK or At.
        // Filter chips on the UI hit (Action, TargetType); list view orders by At desc.
        builder.HasIndex(entry => new
        {
            entry.At,
            entry.Action,
            entry.TargetType,
        });
        // Self-link from a reversal entry to the entry it inverted. Optional FK, no cascade —
        // pruning audit history shouldn't ripple through reversal chains.
        builder
            .HasOne<AuditEntry>()
            .WithMany()
            .HasForeignKey(entry => entry.ReversedByEntryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
