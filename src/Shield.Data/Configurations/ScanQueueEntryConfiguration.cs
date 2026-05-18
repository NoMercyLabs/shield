using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class ScanQueueEntryConfiguration : IEntityTypeConfiguration<ScanQueueEntry>
{
    public void Configure(EntityTypeBuilder<ScanQueueEntry> builder)
    {
        builder.ToTable("ScanQueueEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.SourceId).IsRequired();
        builder.Property(entry => entry.EnqueuedAt).IsRequired();
        builder.Property(entry => entry.ErrorMessage).HasMaxLength(2000);
        builder.Property(entry => entry.Attempts).HasDefaultValue(0);
        builder.Property(entry => entry.DeferredUntil).IsRequired(false);

        // Worker hot path: "oldest pending row, optionally a stale-started row, for any source
        // not currently in flight". The composite index keeps that scan index-only.
        builder.HasIndex(entry => new
        {
            entry.CompletedAt,
            entry.StartedAt,
            entry.EnqueuedAt,
        });
        builder.HasIndex(entry => entry.SourceId);
    }
}
