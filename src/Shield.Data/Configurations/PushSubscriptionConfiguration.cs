using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Endpoint).HasMaxLength(2000).IsRequired();
        builder.Property(subscription => subscription.P256dh).HasMaxLength(200).IsRequired();
        builder.Property(subscription => subscription.Auth).HasMaxLength(200).IsRequired();
        builder.Property(subscription => subscription.UserAgent).HasMaxLength(500);
        // Endpoint is globally unique — the upstream push server picks the URL and never
        // hands the same one to two browsers. UPSERT-by-Endpoint is the insert path.
        builder.HasIndex(subscription => subscription.Endpoint).IsUnique();
        // Per-user lookup is the dispatch hot path: PublishAsync resolves all subscriptions
        // for a target user (or every admin on broadcast) in one query.
        builder.HasIndex(subscription => subscription.UserId);
    }
}
