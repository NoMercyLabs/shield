using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class AlertChannelConfiguration : IEntityTypeConfiguration<AlertChannel>
{
    public void Configure(EntityTypeBuilder<AlertChannel> builder)
    {
        builder.ToTable("AlertChannels");
        builder.HasKey(channel => channel.Id);
        builder.Property(channel => channel.Name).IsRequired().HasMaxLength(200);
        builder.Property(channel => channel.ConfigJsonEncrypted).IsRequired();
    }
}
