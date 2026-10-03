using System.Collections.Concurrent;

namespace Api;

/// <summary>Holds the latest known position of each vehicle, in memory.
/// Written by the MQTT subscriber, read by the HTTP endpoints and the WebSocket hub,
/// so it must be thread-safe.</summary>
public sealed class VehicleStore
{
    private readonly ConcurrentDictionary<string, VehiclePosition> _latest = new();

    /// <summary>Raised after a position was stored. Not raised for outdated messages.
    /// Handlers run on the caller's thread, so they must be fast and must not block.</summary>
    public event Action<VehiclePosition>? PositionUpdated;

    /// <summary>Insert or replace a vehicle's position. Older messages never overwrite newer ones.</summary>
    public void Upsert(VehiclePosition position)
    {
        var stored = _latest.AddOrUpdate(
            position.VehicleId,
            position,
            (_, existing) => position.Timestamp >= existing.Timestamp ? position : existing);

        if (ReferenceEquals(stored, position))
        {
            PositionUpdated?.Invoke(position);
        }
    }

    public IReadOnlyList<VehiclePosition> GetAll() =>
        _latest.Values.OrderBy(v => v.VehicleId, StringComparer.Ordinal).ToList();

    public bool TryGet(string vehicleId, out VehiclePosition? position) =>
        _latest.TryGetValue(vehicleId, out position);
}