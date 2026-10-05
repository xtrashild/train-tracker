namespace Ingestion.Data;

// The timetable (static GTFS), reduced to what the position estimator needs.

public sealed class Stop
{
    public string StopId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public sealed class Route
{
    public string RouteId { get; set; } = "";
    public string? ShortName { get; set; }
    public string? LongName { get; set; }
    public int RouteType { get; set; }
}

public sealed class Trip
{
    public string TripId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string? Headsign { get; set; }
    public string? ServiceId { get; set; }
}

/// <summary>One scheduled stop of one trip. Times are seconds since the start of the service day,
/// and can exceed 86400 for trips that run past midnight (GTFS writes 25:10:00 for 01:10 next day).</summary>
public sealed class StopTime
{
    public string TripId { get; set; } = "";
    public int StopSequence { get; set; }
    public string StopId { get; set; } = "";
    public int? ArrivalSeconds { get; set; }
    public int? DepartureSeconds { get; set; }
}