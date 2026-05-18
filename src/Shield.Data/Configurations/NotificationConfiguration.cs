using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Kind).HasConversion<int>();
        builder.Property(notification => notification.Severity).HasConversion<int>();
        builder.Property(notification => notification.Title).HasMaxLength(300).IsRequired();
        builder.Property(notification => notification.Body).HasMaxLength(4000).IsRequired();
        builder.Property(notification => notification.RelatedType).HasMaxLength(50);
        builder.Property(notification => notification.RelatedId).HasMaxLength(200);
        // Bell-badge hot path: unread-for-user lookup is the dominant query. Composite
        // (UserId, ReadAt, CreatedAt) covers "unread for me, newest first" + the broadcast
        // case (UserId IS NULL) shares the same index thanks to the leading column.
        builder.HasIndex(notification => new
        {
            notification.UserId,
            notification.ReadAt,
            notification.CreatedAt,
        });
        builder.HasIndex(notification => notification.CreatedAt);
    }
}
