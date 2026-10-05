using Microsoft.EntityFrameworkCore;

namespace Ingestion.Data;

public sealed class GtfsDbContext(DbContextOptions<GtfsDbContext> options) : DbContext(options)
{
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<StopTime> StopTimes => Set<StopTime>();

    // Table and column names are set explicitly because the importer bulk-loads with COPY
    // and has to use exactly these names.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Stop>(e =>
        {
            e.ToTable("stops");
            e.HasKey(x => x.StopId);
            e.Property(x => x.StopId).HasColumnName("stop_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Lat).HasColumnName("lat");
            e.Property(x => x.Lon).HasColumnName("lon");
        });

        modelBuilder.Entity<Route>(e =>
        {
            e.ToTable("routes");
            e.HasKey(x => x.RouteId);
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.ShortName).HasColumnName("short_name");
            e.Property(x => x.LongName).HasColumnName("long_name");
            e.Property(x => x.RouteType).HasColumnName("route_type");
        });

        modelBuilder.Entity<Trip>(e =>
        {
            e.ToTable("trips");
            e.HasKey(x => x.TripId);
            e.Property(x => x.TripId).HasColumnName("trip_id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.Headsign).HasColumnName("headsign");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.HasIndex(x => x.RouteId);
            e.HasOne<Route>().WithMany().HasForeignKey(x => x.RouteId);
        });

        modelBuilder.Entity<StopTime>(e =>
        {
            e.ToTable("stop_times");
            // The primary key doubles as the index for "all stops of trip X, in order".
            e.HasKey(x => new { x.TripId, x.StopSequence });
            e.Property(x => x.TripId).HasColumnName("trip_id");
            e.Property(x => x.StopSequence).HasColumnName("stop_sequence");
            e.Property(x => x.StopId).HasColumnName("stop_id");
            e.Property(x => x.ArrivalSeconds).HasColumnName("arrival_seconds");
            e.Property(x => x.DepartureSeconds).HasColumnName("departure_seconds");
            e.HasIndex(x => x.StopId);
            e.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId);
        });
    }
}