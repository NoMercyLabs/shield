using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shield.Data.Design;

public class FeedsDbContextFactory : IDesignTimeDbContextFactory<FeedsDbContext>
{
    public FeedsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<FeedsDbContext> options = new();
        options.UseSqlite("Data Source=feeds.db");
        return new(options.Options);
    }
}
