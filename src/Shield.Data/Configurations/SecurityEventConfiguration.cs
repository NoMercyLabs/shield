using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("SecurityEvents");
        builder.HasKey(securityEvent => securityEvent.Id);
        builder.Property(securityEvent => securityEvent.Source).HasMaxLength(100).IsRequired();
        builder.Property(securityEvent => securityEvent.EventType).HasMaxLength(100).IsRequired();
        builder.Property(securityEvent => securityEvent.Severity).HasConversion<int>();
        builder.Property(securityEvent => securityEvent.Host).HasMaxLength(200);
        builder.Property(securityEvent => securityEvent.Jail).HasMaxLength(100);
        builder.Property(securityEvent => securityEvent.RemoteIp).HasMaxLength(64);
        builder.Property(securityEvent => securityEvent.UserAgent).HasMaxLength(500);
        builder.Property(securityEvent => securityEvent.UserName).HasMaxLength(200);
        builder.Property(securityEvent => securityEvent.Path).HasMaxLength(500);
        // Timeline view sorts by At desc; filter chips (severity, source, jail) pivot on the
        // leading columns. RemoteIp/UserName are filtered alongside but indexed separately
        // because they're often the only filter set when triaging a specific actor.
        builder.HasIndex(securityEvent => new
        {
            securityEvent.At,
            securityEvent.Source,
            securityEvent.Severity,
        });
        builder.HasIndex(securityEvent => securityEvent.RemoteIp);
        builder.HasIndex(securityEvent => securityEvent.Host);
    }
}
