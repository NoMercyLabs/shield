using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> builder)
    {
        builder.ToTable("AlertEvents");
        builder.HasKey(alertEvent => alertEvent.Id);
        builder.Property(alertEvent => alertEvent.Error).HasMaxLength(2000);

        builder.HasIndex(alertEvent => new { alertEvent.FindingId, alertEvent.SentAt });
        builder.HasIndex(alertEvent => alertEvent.ChannelId);
    }
}
