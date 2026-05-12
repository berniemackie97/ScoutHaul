using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ScoutHaul.Infrastructure.Persistence;

public sealed class ScoutHaulDbContextFactory : IDesignTimeDbContextFactory<ScoutHaulDbContext>
{
    public ScoutHaulDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ScoutHaulDbContext> options = new DbContextOptionsBuilder<ScoutHaulDbContext>();
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                                  ?? "Host=localhost;Port=5433;Database=scouthaul;Username=scouthaul;Password=scouthaul";

        options.UseNpgsql(connectionString);
        return new ScoutHaulDbContext(options.Options);
    }
}
