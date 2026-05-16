using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shield.Core.Domain;

namespace Shield.Data.Configurations;

public class AgentTokenConfiguration : IEntityTypeConfiguration<AgentToken>
{
    public void Configure(EntityTypeBuilder<AgentToken> builder)
    {
        builder.ToTable("AgentTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(256);
        builder.HasIndex(token => token.HostId);
        builder.HasIndex(token => token.TokenHash);
    }
}
