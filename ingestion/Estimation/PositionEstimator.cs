namespace Ingestion.Estimation;

/// <summary>Estimates where a train is right now, from its timetable and the delays reported for it.
///
/// There is no GPS in the data, so the position is reconstructed: the planned times are shifted
/// by the reported delays, then the train is placed along the straight line between the two
/// stations it is between, in proportion to the elapsed time.
///
/// The method is a pure function of its inputs, so it is easy to test.</summary>
public static class PositionEstimator
{
    /// <param name="schedule">The stops of the trip in order.</param>
    /// <param name="updates">Delays from the realtime feed, in order. May be empty or cover only some stops.</param>
    /// <param name="nowSeconds">The current time as seconds since the start of the service day.</param>
    /// <returns>The estimated position, or null when the trip is not running at that moment.</returns>
    public static EstimatedPosition? Estimate(
        IReadOnlyList<ScheduledStop> schedule,
        IReadOnlyList<StopDelay> updates,
        int nowSeconds)
    {
        // Skipped stops are removed from the route (matched by stop id).
        var skipped = updates.Where(u => u.Skipped).Select(u => u.StopId).ToHashSet();
        var stops = skipped.Count == 0 ? schedule : schedule.Where(s => !skipped.Contains(s.StopId)).ToList();
        if (stops.Count < 2)
        {
            return null;
        }

        var (arrivalDelay, departureDelay) = ResolveDelays(stops, updates.Where(u => !u.Skipped).ToList());

        // Actual times = planned times + delay, with two physical rules:
        //  - a train does not leave before its planned departure (even if it arrived early),
        //  - time never runs backwards along the route.
        var n = stops.Count;
        var arrival = new int[n];
        var departure = new int[n];
        for (var i = 0; i < n; i++)
        {
            arrival[i] = stops[i].ArrivalSeconds + arrivalDelay[i];
            if (i > 0)
            {
                arrival[i] = Math.Max(arrival[i], departure[i - 1]);
            }

            var plannedDeparture = stops[i].DepartureSeconds;
            departure[i] = Math.Max(plannedDeparture + Math.Max(0, departureDelay[i]), arrival[i]);
        }

        if (nowSeconds < arrival[0] || nowSeconds > departure[n - 1])
        {
            return null;
        }

        for (var i = 0; i < n; i++)
        {
            var delay = departure[i] - stops[i].DepartureSeconds;

            if (nowSeconds >= arrival[i] && nowSeconds <= departure[i])
            {
                return new EstimatedPosition(
                    stops[i].Lat, stops[i].Lon, PositionKind.AtStop, delay, 0, stops[i].StopId, stops[i].StopId);
            }

            if (i < n - 1 && nowSeconds > departure[i] && nowSeconds < arrival[i + 1])
            {
                var from = stops[i];
                var to = stops[i + 1];
                var duration = arrival[i + 1] - departure[i];
                var fraction = (nowSeconds - departure[i]) / (double)duration;
                var metres = Geo.DistanceMetres(from.Lat, from.Lon, to.Lat, to.Lon);

                return new EstimatedPosition(
                    from.Lat + (to.Lat - from.Lat) * fraction,
                    from.Lon + (to.Lon - from.Lon) * fraction,
                    PositionKind.BetweenStops,
                    delay,
                    metres / duration * 3.6,
                    from.StopId,
                    to.StopId);
            }
        }

        return null;
    }

    /// <summary>Works out an arrival and departure delay for every stop.
    /// A reported delay holds for the following stops until the next report (GTFS-Realtime rule).
    /// Stops before the first report take that first delay, so the train does not jump.</summary>
    private static (int[] Arrival, int[] Departure) ResolveDelays(
        IReadOnlyList<ScheduledStop> stops,
        IReadOnlyList<StopDelay> updates)
    {
        var n = stops.Count;
        var knownArrival = new int?[n];
        var knownDeparture = new int?[n];

        // Updates are matched to stops by id, always searching forward, so that a stop id that
        // appears twice on a looping line is not matched to the wrong visit.
        var searchFrom = 0;
        foreach (var update in updates)
        {
            var index = IndexOfStop(stops, update.StopId, searchFrom);
            if (index < 0)
            {
                continue; // unknown stop: ignore
            }

            knownArrival[index] = update.ArrivalDelaySeconds ?? update.DepartureDelaySeconds;
            knownDeparture[index] = update.DepartureDelaySeconds ?? update.ArrivalDelaySeconds;
            searchFrom = index + 1;
        }

        var carry = 0;
        for (var i = 0; i < n; i++)
        {
            if (knownDeparture[i] is { } first)
            {
                carry = first;
                break;
            }
        }

        var arrival = new int[n];
        var departure = new int[n];
        for (var i = 0; i < n; i++)
        {
            arrival[i] = knownArrival[i] ?? carry;
            departure[i] = knownDeparture[i] ?? arrival[i];
            carry = departure[i];
        }

        return (arrival, departure);
    }

    private static int IndexOfStop(IReadOnlyList<ScheduledStop> stops, string stopId, int from)
    {
        for (var i = from; i < stops.Count; i++)
        {
            if (stops[i].StopId == stopId)
            {
                return i;
            }
        }
        return -1;
    }
}