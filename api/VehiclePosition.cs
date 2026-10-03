namespace Api;

/// <summary>One position report, as published by the simulator (and later by the estimator).
/// Property names match the camelCase JSON on the MQTT topic.</summary>
public sealed record VehiclePosition(
    string VehicleId,
    double Lat,
    double Lon,
    string LineId,
    int DelaySeconds,
    bool Estimated,
    double SpeedKmh,
    DateTimeOffset Timestamp);