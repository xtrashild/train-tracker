namespace Ingestion.Data;

public static class Db
{
    /// <summary>Taken from the ConnectionStrings__Gtfs environment variable, with a local default
    /// that matches docker-compose.yml.</summary>
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Gtfs")
        ?? "Host=localhost;Port=5432;Database=tracker;Username=tracker;Password=tracker";
}