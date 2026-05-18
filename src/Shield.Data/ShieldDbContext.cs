using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Data;

public class ShieldDbContext : IdentityDbContext<ShieldUser, ShieldRole, Guid>
{
    public ShieldDbContext(DbContextOptions<ShieldDbContext> options)
        : base(options) { }

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<AlertChannel> AlertChannels => Set<AlertChannel>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SourceGroup> SourceGroups => Set<SourceGroup>();
    public DbSet<SourceAccess> SourceAccesses => Set<SourceAccess>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PackageWatch> PackageWatches => Set<PackageWatch>();
    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<ScanQueueEntry> ScanQueueEntries => Set<ScanQueueEntry>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<IpReputation> IpReputations => Set<IpReputation>();
    public DbSet<PackageUpdate> PackageUpdates => Set<PackageUpdate>();

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
        modelBuilder.ApplyConfiguration(new Configurations.AppSettingConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.IntegrationTokenConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.AuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.SourceGroupConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.SourceAccessConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.GroupMembershipConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.UserSessionConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.NotificationConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PackageWatchConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.SavedFilterConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ApiTokenConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.ScanQueueEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.InviteConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PushSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.SecurityEventConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.IpReputationConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.PackageUpdateConfiguration());
    }
}
