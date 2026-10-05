using System.Diagnostics;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace Ingestion.Gtfs;

/// <summary>Loads a region of a static GTFS feed (an unzipped folder) into PostgreSQL.
///
/// Region selection: a trip is imported if at least one of its stops lies inside the bounding box.
/// All stop times of an imported trip are kept, also those outside the box, so that a train
/// can be followed across the border of the region.
///
/// The import replaces the previous contents of the timetable tables, in one transaction.</summary>
public sealed class GtfsImporter
{
    private readonly string _connectionString;
    private readonly string _directory;
    private readonly BoundingBox _box;
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public GtfsImporter(string connectionString, string directory, BoundingBox box)
    {
        _connectionString = connectionString;
        _directory = directory;
        _box = box;
    }

    private sealed record StopRow(string Id, string Name, double Lat, double Lon);
    private sealed record RouteRow(string Id, string? ShortName, string? LongName, int Type);
    private sealed record TripRow(string Id, string RouteId, string? Headsign, string? ServiceId);

    public async Task RunAsync(CancellationToken ct = default)
    {
        Log($"Importing {_directory} for bounding box {_box}");

        Log("Reading stops.txt");
        var stops = ReadStops();
        var stopsInBox = stops.Values.Where(s => _box.Contains(s.Lat, s.Lon)).Select(s => s.Id).ToHashSet();
        Log($"  {stops.Count} stops, {stopsInBox.Count} inside the box");
        if (stopsInBox.Count == 0)
        {
            throw new InvalidOperationException("No stops inside the bounding box. Check --bbox (order: minLat,minLon,maxLat,maxLon).");
        }

        Log("Pass 1 of 2 over stop_times.txt: finding trips that stop inside the box");
        var candidateTrips = FindTripsInBox(stopsInBox);
        Log($"  {candidateTrips.Count} candidate trips");

        Log("Reading routes.txt and trips.txt");
        var routes = ReadRoutes();
        var trips = ReadTrips(candidateTrips, routes);
        var tripIds = trips.Select(t => t.Id).ToHashSet();
        var routeIds = trips.Select(t => t.RouteId).ToHashSet();
        Log($"  {trips.Count} trips on {routeIds.Count} routes");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        Log("Clearing old timetable data");
        await using (var truncate = new NpgsqlCommand("TRUNCATE stop_times, trips, routes, stops", connection, transaction))
        {
            await truncate.ExecuteNonQueryAsync(ct);
        }

        Log("Writing routes");
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY routes (route_id, short_name, long_name, route_type) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var id in routeIds)
            {
                var r = routes[id];
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(r.Id, NpgsqlDbType.Text, ct);
                await WriteTextAsync(writer, r.ShortName, ct);
                await WriteTextAsync(writer, r.LongName, ct);
                await writer.WriteAsync(r.Type, NpgsqlDbType.Integer, ct);
            }
            await writer.CompleteAsync(ct);
        }

        Log("Writing trips");
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY trips (trip_id, route_id, headsign, service_id) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var t in trips)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(t.Id, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(t.RouteId, NpgsqlDbType.Text, ct);
                await WriteTextAsync(writer, t.Headsign, ct);
                await WriteTextAsync(writer, t.ServiceId, ct);
            }
            await writer.CompleteAsync(ct);
        }

        Log("Pass 2 of 2 over stop_times.txt: writing stop times");
        var usedStops = new HashSet<string>();
        long written = 0;
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY stop_times (trip_id, stop_sequence, stop_id, arrival_seconds, departure_seconds) FROM STDIN (FORMAT BINARY)", ct))
        {
            using var csv = Open("stop_times.txt");
            long rows = 0;
            while (csv.Read())
            {
                if (++rows % 2_000_000 == 0)
                {
                    Log($"  {rows:N0} rows read, {written:N0} written");
                }

                var tripId = csv.GetField("trip_id");
                if (tripId is null || !tripIds.Contains(tripId))
                {
                    continue;
                }

                var stopId = csv.GetField("stop_id");
                if (stopId is null || !stops.ContainsKey(stopId))
                {
                    continue;
                }

                if (!int.TryParse(csv.GetField("stop_sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence))
                {
                    continue;
                }

                var arrival = GtfsTime.ParseSeconds(csv.GetField("arrival_time"));
                var departure = GtfsTime.ParseSeconds(csv.GetField("departure_time"));
                arrival ??= departure;
                departure ??= arrival;

                await writer.StartRowAsync(ct);
                await writer.WriteAsync(tripId, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(sequence, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(stopId, NpgsqlDbType.Text, ct);
                await WriteIntAsync(writer, arrival, ct);
                await WriteIntAsync(writer, departure, ct);

                usedStops.Add(stopId);
                written++;
            }
            await writer.CompleteAsync(ct);
            Log($"  {rows:N0} rows read, {written:N0} written");
        }

        Log("Writing stops");
        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY stops (stop_id, name, lat, lon) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var id in usedStops)
            {
                var s = stops[id];
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(s.Id, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(s.Name, NpgsqlDbType.Text, ct);
                await writer.WriteAsync(s.Lat, NpgsqlDbType.Double, ct);
                await writer.WriteAsync(s.Lon, NpgsqlDbType.Double, ct);
            }
            await writer.CompleteAsync(ct);
        }

        await transaction.CommitAsync(ct);

        await using (var analyze = new NpgsqlCommand("ANALYZE", connection))
        {
            await analyze.ExecuteNonQueryAsync(ct);
        }

        Log($"Done: {routeIds.Count} routes, {trips.Count} trips, {written:N0} stop times, {usedStops.Count} stops");
    }

    private Dictionary<string, StopRow> ReadStops()
    {
        var result = new Dictionary<string, StopRow>();
        using var csv = Open("stops.txt");
        while (csv.Read())
        {
            var id = csv.GetField("stop_id");
            if (string.IsNullOrEmpty(id)
                || !TryDouble(csv.GetField("stop_lat"), out var lat)
                || !TryDouble(csv.GetField("stop_lon"), out var lon))
            {
                continue; // stops without coordinates cannot be placed on a map
            }
            result[id] = new StopRow(id, csv.GetField("stop_name") ?? "", lat, lon);
        }
        return result;
    }

    private HashSet<string> FindTripsInBox(HashSet<string> stopsInBox)
    {
        var trips = new HashSet<string>();
        using var csv = Open("stop_times.txt");
        long rows = 0;
        while (csv.Read())
        {
            if (++rows % 2_000_000 == 0)
            {
                Log($"  {rows:N0} rows read");
            }

            var stopId = csv.GetField("stop_id");
            if (stopId is not null && stopsInBox.Contains(stopId))
            {
                var tripId = csv.GetField("trip_id");
                if (tripId is not null)
                {
                    trips.Add(tripId);
                }
            }
        }
        Log($"  {rows:N0} rows read");
        return trips;
    }

    private Dictionary<string, RouteRow> ReadRoutes()
    {
        var result = new Dictionary<string, RouteRow>();
        using var csv = Open("routes.txt");
        while (csv.Read())
        {
            var id = csv.GetField("route_id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }
            _ = int.TryParse(csv.GetField("route_type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var type);
            result[id] = new RouteRow(id, NullIfEmpty(csv.GetField("route_short_name")), NullIfEmpty(csv.GetField("route_long_name")), type);
        }
        return result;
    }

    private List<TripRow> ReadTrips(HashSet<string> candidateTrips, Dictionary<string, RouteRow> routes)
    {
        var result = new List<TripRow>();
        using var csv = Open("trips.txt");
        while (csv.Read())
        {
            var tripId = csv.GetField("trip_id");
            var routeId = csv.GetField("route_id");
            if (tripId is null || routeId is null || !candidateTrips.Contains(tripId) || !routes.ContainsKey(routeId))
            {
                continue;
            }
            result.Add(new TripRow(tripId, routeId, NullIfEmpty(csv.GetField("trip_headsign")), NullIfEmpty(csv.GetField("service_id"))));
        }
        return result;
    }

    private CsvReader Open(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{fileName} not found in {_directory}. Is this the unzipped GTFS folder?", path);
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
        };
        var csv = new CsvReader(new StreamReader(path), config);
        if (!csv.Read() || !csv.ReadHeader())
        {
            csv.Dispose();
            throw new InvalidDataException($"{fileName} is empty or has no header row.");
        }
        return csv;
    }

    private static bool TryDouble(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task WriteTextAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken ct)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(ct);
        }
        else
        {
            await writer.WriteAsync(value, NpgsqlDbType.Text, ct);
        }
    }

    private static async Task WriteIntAsync(NpgsqlBinaryImporter writer, int? value, CancellationToken ct)
    {
        if (value is null)
        {
            await writer.WriteNullAsync(ct);
        }
        else
        {
            await writer.WriteAsync(value.Value, NpgsqlDbType.Integer, ct);
        }
    }

    private void Log(string message) =>
        Console.WriteLine($"[{_watch.Elapsed.ToString(@"mm\:ss")}] {message}");
}