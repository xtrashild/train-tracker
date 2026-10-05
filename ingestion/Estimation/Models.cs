namespace Ingestion.Estimation;

/// <summary>One stop of a trip as planned in the timetable. Times are seconds since the start of
/// the service day.</summary>
public sealed record ScheduledStop(string StopId, double Lat, double Lon, int ArrivalSeconds, int DepartureSeconds);

/// <summary>What the realtime feed says about one stop of a trip: how late the train arrives and
/// leaves (negative means early), or that the stop is skipped.</summary>
public sealed record StopDelay(string StopId, int? ArrivalDelaySeconds, int? DepartureDelaySeconds, bool Skipped = false);

public enum PositionKind
{
    /// <summary>Waiting at a station.</summary>
    AtStop,

    /// <summary>On the way between two stations.</summary>
    BetweenStops,
}

public sealed record EstimatedPosition(
    double Lat,
    double Lon,
    PositionKind Kind,
    int DelaySeconds,
    double SpeedKmh,
    string FromStopId,
    string ToStopId);