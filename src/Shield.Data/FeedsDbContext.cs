using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;

namespace Shield.Data;

public class FeedsDbContext : DbContext
{
    public FeedsDbContext(DbContextOptions<FeedsDbContext> options)
        : base(options) { }

    public DbSet<Advisory> Advisories => Set<Advisory>();
    public DbSet<PackageMeta> PackageMetas => Set<PackageMeta>();
    public DbSet<FeedSyncState> FeedSyncStates => Set<FeedSyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new Configurations.AdvisoryConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PackageMetaConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.FeedSyncStateConfiguration());
    }
}
