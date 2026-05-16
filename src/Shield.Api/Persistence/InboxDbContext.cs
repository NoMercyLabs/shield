using Microsoft.EntityFrameworkCore;
using Shield.Channels.Inbox;

namespace Shield.Api.Persistence;

public class InboxDbContext : DbContext
{
    public InboxDbContext(DbContextOptions<InboxDbContext> options)
        : base(options) { }

    public DbSet<InboxMessage> Messages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("InboxMessages");
            builder.HasKey(message => message.Id);
            builder.Property(message => message.Title).IsRequired().HasMaxLength(500);
            builder.Property(message => message.Body).IsRequired();
            builder.HasIndex(message => message.CreatedAt);
            builder.HasIndex(message => message.Read);
        });
    }
}
