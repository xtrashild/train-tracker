using System.Collections.Concurrent;

namespace Api;

/// <summary>Holds the latest known position of each vehicle, in memory.
/// Written by the MQTT subscriber, read by the HTTP endpoints, so it must be thread-safe.</summary>
public sealed class VehicleStore
{
    private readonly ConcurrentDictionary<string, VehiclePosition> _latest = new();

    /// <summary>Insert or replace a vehicle's position. Older messages never overwrite newer ones.</summary>
    public void Upsert(VehiclePosition position) =>
        _latest.AddOrUpdate(
            position.VehicleId,
            position,
            (_, existing) => position.Timestamp >= existing.Timestamp ? position : existing);

    public IReadOnlyList<VehiclePosition> GetAll() =>
        _latest.Values.OrderBy(v => v.VehicleId, StringComparer.Ordinal).ToList();

    public bool TryGet(string vehicleId, out VehiclePosition? position) =>
        _latest.TryGetValue(vehicleId, out position);
}