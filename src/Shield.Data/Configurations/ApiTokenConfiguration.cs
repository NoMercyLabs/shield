using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("ApiTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Name).IsRequired().HasMaxLength(200);
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(token => token.Prefix).IsRequired().HasMaxLength(16);
        builder.Property(token => token.Scopes).IsRequired().HasMaxLength(500);
        builder.Property(token => token.SourceIdFilter).IsRequired().HasMaxLength(2000);
        builder.Property(token => token.LastUsedIp).HasMaxLength(64);
        builder.HasIndex(token => token.UserId);
        builder.HasIndex(token => token.TokenHash).IsUnique();
    }
}
