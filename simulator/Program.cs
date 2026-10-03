using System.Globalization;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

// ---- Configuration (override with environment variables) -------------------
var host = Env("MQTT_HOST", "localhost");
var port = int.Parse(Env("MQTT_PORT", "1883"), CultureInfo.InvariantCulture);
var intervalMs = int.Parse(Env("PUBLISH_INTERVAL_MS", "2000"), CultureInfo.InvariantCulture);
// 20 means: the trains move 20x faster than real time, so movement is visible on a map.
var timeScale = double.Parse(Env("TIME_SCALE", "20"), CultureInfo.InvariantCulture);

// ---- Routes: simplified waypoints (approximate coordinates, straight lines) --
var routes = new[]
{
    new Route("RE1", new[] { (51.0405, 13.7320), (50.9159, 13.3427), (50.8403, 12.9296) }, 4),  // Dresden - Freiberg - Chemnitz
    new Route("S1", new[] { (51.0405, 13.7320), (50.9616, 13.9370), (50.9182, 14.1500) }, 3),   // Dresden - Pirna - Bad Schandau
    new Route("RE50", new[] { (51.0405, 13.7320), (51.1670, 13.4730), (51.3085, 13.2915) }, 3), // Dresden - Meissen - Riesa
};

// ---- Create vehicles, spread out along their routes -------------------------
var rng = new Random(42);
var vehicles = new List<Vehicle>();
foreach (var route in routes)
{
    for (var i = 0; i < route.VehicleCount; i++)
    {
        var id = $"{route.LineId}-{i + 1:00}";
        var speedKmh = 60 + rng.NextDouble() * 60;          // 60 to 120 km/h
        var startFraction = (double)i / route.VehicleCount;  // staggered start
        vehicles.Add(new Vehicle(id, route.LineId, route.Points, speedKmh, startFraction, rng.Next()));
    }
}

// ---- MQTT client -------------------------------------------------------------
var factory = new MqttFactory();
using var client = factory.CreateMqttClient();
var options = new MqttClientOptionsBuilder()
    .WithTcpServer(host, port)
    .WithClientId($"simulator-{Guid.NewGuid():N}")
    .Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
Console.WriteLine($"Simulating {vehicles.Count} vehicles -> mqtt://{host}:{port}, every {intervalMs} ms, time scale x{timeScale}");

var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
var last = DateTime.UtcNow;

try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        await EnsureConnected();

        var now = DateTime.UtcNow;
        var dt = (now - last).TotalSeconds;
        last = now;

        var sent = 0;
        foreach (var v in vehicles)
        {
            v.Step(dt, timeScale);
            var (lat, lon) = v.Position();

            var payload = JsonSerializer.Serialize(new VehicleMessage(
                VehicleId: v.Id,
                Lat: Math.Round(lat, 6),
                Lon: Math.Round(lon, 6),
                LineId: v.LineId,
                DelaySeconds: v.DelaySeconds,
                Estimated: false,
                SpeedKmh: Math.Round(v.SpeedKmh, 1),
                Timestamp: DateTimeOffset.UtcNow), json);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic($"vehicles/{v.Id}/position")
                .WithPayload(payload)
                .WithRetainFlag(true) // new subscribers immediately get the last known position
                .Build();

            try
            {
                await client.PublishAsync(message, cts.Token);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Publish failed for {v.Id}: {ex.Message}");
                break; // reconnect on the next tick
            }
        }

        Console.WriteLine($"{DateTime.Now:HH:mm:ss} published {sent}/{vehicles.Count}");
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C
}

if (client.IsConnected)
{
    await client.DisconnectAsync();
}
Console.WriteLine("Stopped.");

// ---- Helpers -----------------------------------------------------------------
async Task EnsureConnected()
{
    while (!client.IsConnected && !cts.IsCancellationRequested)
    {
        try
        {
            await client.ConnectAsync(options, cts.Token);
            Console.WriteLine("Connected to broker.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"Cannot connect to broker ({ex.Message}). Retrying in 2 s...");
            await Task.Delay(2000, cts.Token);
        }
    }
}

static string Env(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

// ---- Types -------------------------------------------------------------------
record Route(string LineId, (double Lat, double Lon)[] Points, int VehicleCount);

record VehicleMessage(
    string VehicleId,
    double Lat,
    double Lon,
    string LineId,
    int DelaySeconds,
    bool Estimated,
    double SpeedKmh,
    DateTimeOffset Timestamp);

/// <summary>A vehicle that shuttles back and forth along a polyline of waypoints.</summary>
class Vehicle
{
    private readonly (double Lat, double Lon)[] _points;
    private readonly double[] _cumulativeMetres; // distance from the start to each waypoint
    private readonly double _totalMetres;
    private readonly Random _rng;
    private double _alongMetres;
    private int _direction = 1;

    public string Id { get; }
    public string LineId { get; }
    public double SpeedKmh { get; }
    public int DelaySeconds { get; private set; }

    public Vehicle(string id, string lineId, (double Lat, double Lon)[] points,
                   double speedKmh, double startFraction, int seed)
    {
        Id = id;
        LineId = lineId;
        SpeedKmh = speedKmh;
        _points = points;
        _rng = new Random(seed);

        _cumulativeMetres = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            _cumulativeMetres[i] = _cumulativeMetres[i - 1] + Geo.HaversineMetres(points[i - 1], points[i]);
        }
        _totalMetres = _cumulativeMetres[^1];
        _alongMetres = _totalMetres * Math.Clamp(startFraction, 0, 1);
    }

    /// <summary>Advance the vehicle by realSeconds of wall-clock time, multiplied by timeScale.</summary>
    public void Step(double realSeconds, double timeScale)
    {
        var metres = SpeedKmh / 3.6 * realSeconds * timeScale;
        _alongMetres += _direction * metres;

        if (_alongMetres >= _totalMetres) { _alongMetres = _totalMetres; _direction = -1; }
        else if (_alongMetres <= 0) { _alongMetres = 0; _direction = 1; }

        // Random walk for the delay, limited to 0..15 minutes.
        DelaySeconds = Math.Clamp(DelaySeconds + _rng.Next(-10, 11), 0, 900);
    }

    public (double Lat, double Lon) Position()
    {
        for (var i = 0; i < _cumulativeMetres.Length - 1; i++)
        {
            if (_alongMetres <= _cumulativeMetres[i + 1])
            {
                var segment = _cumulativeMetres[i + 1] - _cumulativeMetres[i];
                var f = segment <= 0 ? 0 : (_alongMetres - _cumulativeMetres[i]) / segment;
                var a = _points[i];
                var b = _points[i + 1];
                return (a.Lat + (b.Lat - a.Lat) * f, a.Lon + (b.Lon - a.Lon) * f);
            }
        }
        return _points[^1];
    }
}

static class Geo
{
    public static double HaversineMetres((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        const double R = 6_371_000; // Earth radius in metres
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad(b.Lat - a.Lat);
        var dLon = Rad(b.Lon - a.Lon);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }
}