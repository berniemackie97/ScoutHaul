using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HaulScout.Infrastructure.Persistence;

public sealed class HaulScoutDbContextFactory : IDesignTimeDbContextFactory<HaulScoutDbContext>
{
    public HaulScoutDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>();
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                                  ?? "Host=localhost;Port=5433;Database=scouthaul;Username=scouthaul;Password=scouthaul";

        options.UseNpgsql(connectionString);
        return new HaulScoutDbContext(options.Options);
    }
}
