using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class IntegrationTokenConfiguration : IEntityTypeConfiguration<IntegrationToken>
{
    public void Configure(EntityTypeBuilder<IntegrationToken> builder)
    {
        builder.ToTable("IntegrationTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Provider).HasConversion<int>();
        builder.Property(token => token.Subject).HasMaxLength(200).IsRequired();
        builder.Property(token => token.AccessTokenEncrypted).IsRequired();
        builder.Property(token => token.Scopes).HasMaxLength(1000);
        builder.Property(token => token.AccountLogin).HasMaxLength(200);
        builder.Property(token => token.AccountId).HasMaxLength(200);
        // Non-unique lookups: callback finds existing row by (Provider, Subject);
        // accessors find a user's row by (Provider, LinkedUserId).
        builder.HasIndex(token => new { token.Provider, token.Subject });
        builder.HasIndex(token => new { token.Provider, token.LinkedUserId });
    }
}
