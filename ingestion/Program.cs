using Ingestion.Data;
using Ingestion.Gtfs;
using Microsoft.EntityFrameworkCore;

const string Usage = """
    Usage:
      dotnet run -- migrate
          Create or update the database schema.

      dotnet run -- import --gtfs <folder> [--bbox minLat,minLon,maxLat,maxLon]
          Import a region of an unzipped GTFS feed (runs migrate first).
          Default bbox: 50.7,12.8,51.4,14.3 (Dresden and Chemnitz area)
    """;

var command = args.FirstOrDefault();

try
{
    switch (command)
    {
        case "migrate":
            await MigrateAsync();
            return 0;

        case "import":
        {
            var folder = ArgValue("--gtfs");
            if (folder is null)
            {
                Console.WriteLine(Usage);
                return 1;
            }

            var bboxText = ArgValue("--bbox");
            var box = bboxText is null ? BoundingBox.Default : BoundingBox.Parse(bboxText);

            await MigrateAsync();
            await new GtfsImporter(Db.ConnectionString, folder, box).RunAsync();
            return 0;
        }

        default:
            Console.WriteLine(Usage);
            return 1;
    }
}
catch (Exception ex) when (ex is FormatException or FileNotFoundException or InvalidDataException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

string? ArgValue(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static async Task MigrateAsync()
{
    var options = new DbContextOptionsBuilder<GtfsDbContext>().UseNpgsql(Db.ConnectionString).Options;
    await using var db = new GtfsDbContext(options);
    await db.Database.MigrateAsync();
    Console.WriteLine("Database schema is up to date.");
}