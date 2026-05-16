using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shield.Data.Design;

public class ShieldDbContextFactory : IDesignTimeDbContextFactory<ShieldDbContext>
{
    public ShieldDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ShieldDbContext> options = new();
        options.UseSqlite("Data Source=shield.db");
        return new ShieldDbContext(options.Options);
    }
}
