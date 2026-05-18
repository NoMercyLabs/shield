using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(session => session.UserAgent).HasMaxLength(512);
        builder.Property(session => session.RemoteIp).HasMaxLength(64);
        // SessionTrackingMiddleware looks up by TokenHash on every authenticated request;
        // SessionsController lists by UserId.
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => new { session.UserId, session.RevokedAt });
    }
}
