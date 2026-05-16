using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Data;

public class ShieldDbContext : IdentityDbContext<ShieldUser, ShieldRole, Guid>
{
    public ShieldDbContext(DbContextOptions<ShieldDbContext> options) : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<AlertChannel> AlertChannels => Set<AlertChannel>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new Configurations.SourceConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.InventorySnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.InventoryItemConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.FindingConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AlertChannelConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AlertEventConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AgentTokenConfiguration());
    }
}
