using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("WebhookEndpoints");
        builder.HasKey(endpoint => endpoint.Id);
        builder.Property(endpoint => endpoint.Label).IsRequired().HasMaxLength(200);
        builder.Property(endpoint => endpoint.SecretEncrypted).IsRequired();
        builder.Property(endpoint => endpoint.LastDeliveryStatus).HasMaxLength(64);
        builder.HasIndex(endpoint => endpoint.Provider);
        builder.HasIndex(endpoint => endpoint.CreatedAt);
    }
}
