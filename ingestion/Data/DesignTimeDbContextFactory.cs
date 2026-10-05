using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingestion.Data;

/// <summary>Used only by `dotnet ef` (for example `dotnet ef migrations add`) to create the context.
/// It does not connect to the database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GtfsDbContext>
{
    public GtfsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GtfsDbContext>()
            .UseNpgsql(Db.ConnectionString)
            .Options;
        return new GtfsDbContext(options);
    }
}