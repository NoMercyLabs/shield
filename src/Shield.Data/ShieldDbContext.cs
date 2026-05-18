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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new Configurations.SourceConfiguration());
        builder.ApplyConfiguration(new Configurations.InventorySnapshotConfiguration());
        builder.ApplyConfiguration(new Configurations.InventoryItemConfiguration());
        builder.ApplyConfiguration(new Configurations.FindingConfiguration());
        builder.ApplyConfiguration(new Configurations.AlertChannelConfiguration());
        builder.ApplyConfiguration(new Configurations.AlertEventConfiguration());
        builder.ApplyConfiguration(new Configurations.AgentTokenConfiguration());
        builder.ApplyConfiguration(new Configurations.AppSettingConfiguration());
        builder.ApplyConfiguration(new Configurations.IntegrationTokenConfiguration());
        builder.ApplyConfiguration(new Configurations.AuditEntryConfiguration());
        builder.ApplyConfiguration(new Configurations.SourceGroupConfiguration());
        builder.ApplyConfiguration(new Configurations.SourceAccessConfiguration());
        builder.ApplyConfiguration(new Configurations.GroupMembershipConfiguration());
        builder.ApplyConfiguration(new Configurations.UserSessionConfiguration());
        builder.ApplyConfiguration(new Configurations.NotificationConfiguration());
        builder.ApplyConfiguration(new Configurations.PackageWatchConfiguration());
        builder.ApplyConfiguration(new Configurations.SavedFilterConfiguration());
        builder.ApplyConfiguration(new Configurations.ApiTokenConfiguration());
        builder.ApplyConfiguration(new Configurations.ScanQueueEntryConfiguration());
        builder.ApplyConfiguration(new Configurations.InviteConfiguration());
        builder.ApplyConfiguration(new Configurations.PushSubscriptionConfiguration());
        builder.ApplyConfiguration(new Configurations.SecurityEventConfiguration());
        builder.ApplyConfiguration(new Configurations.IpReputationConfiguration());
        builder.ApplyConfiguration(new Configurations.PackageUpdateConfiguration());
    }
}
