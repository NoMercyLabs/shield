using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public sealed class WebhookEnvelopeConfiguration : IEntityTypeConfiguration<WebhookEnvelope>
{
    public void Configure(EntityTypeBuilder<WebhookEnvelope> builder)
    {
        builder.ToTable("WebhookEnvelopes");
        builder.HasKey(envelope => envelope.Id);
        builder.Property(envelope => envelope.EventType).HasMaxLength(64);
        builder.Property(envelope => envelope.DeliveryId).HasMaxLength(128);
        builder.Property(envelope => envelope.HeadersJson).IsRequired();
        builder.Property(envelope => envelope.PayloadJson).IsRequired();
        builder.Property(envelope => envelope.Reason).HasMaxLength(64);
        builder.HasIndex(envelope => new { envelope.EndpointId, envelope.ReceivedAt });
        builder.HasIndex(envelope => envelope.ReceivedAt);
        builder
            .HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(envelope => envelope.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
