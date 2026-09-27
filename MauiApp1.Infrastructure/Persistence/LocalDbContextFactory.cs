using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MauiApp1.Infrastructure.Persistence;

public sealed class LocalDbContextFactory : IDesignTimeDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite("Data Source=miscelanea-ari-design-time.db")
            .Options;

        return new LocalDbContext(options);
    }
}
