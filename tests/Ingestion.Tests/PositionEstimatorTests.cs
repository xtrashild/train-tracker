using Ingestion.Estimation;
using Xunit;

namespace Ingestion.Tests;

public class PositionEstimatorTests
{
    private static int T(int hours, int minutes = 0) => hours * 3600 + minutes * 60;

    // A (50.0) 08:00  ->  B (50.1) arrives 08:10, leaves 08:12  ->  C (50.2) arrives 08:30
    private static readonly ScheduledStop A = new("A", 50.0, 13.0, T(8), T(8));
    private static readonly ScheduledStop B = new("B", 50.1, 13.0, T(8, 10), T(8, 12));
    private static readonly ScheduledStop C = new("C", 50.2, 13.0, T(8, 30), T(8, 30));
    private static readonly ScheduledStop[] Trip = { A, B, C };

    private static readonly StopDelay[] NoUpdates = Array.Empty<StopDelay>();

    private static EstimatedPosition Locate(IReadOnlyList<ScheduledStop> trip, IReadOnlyList<StopDelay> updates, int now)
    {
        var position = PositionEstimator.Estimate(trip, updates, now);
        Assert.NotNull(position);
        return position!;
    }

    [Fact]
    public void NotRunning_BeforeFirstArrival_ReturnsNull() =>
        Assert.Null(PositionEstimator.Estimate(Trip, NoUpdates, T(7, 59)));

    [Fact]
    public void NotRunning_AfterLastDeparture_ReturnsNull() =>
        Assert.Null(PositionEstimator.Estimate(Trip, NoUpdates, T(8, 31)));

    [Fact]
    public void FewerThanTwoStops_ReturnsNull() =>
        Assert.Null(PositionEstimator.Estimate(new[] { A }, NoUpdates, T(8)));

    [Fact]
    public void OnTime_WaitingAtStation_IsPlacedAtTheStation()
    {
        var position = Locate(Trip, NoUpdates, T(8, 11));

        Assert.Equal(PositionKind.AtStop, position.Kind);
        Assert.Equal(50.1, position.Lat, 6);
        Assert.Equal("B", position.FromStopId);
        Assert.Equal(0, position.SpeedKmh, 6);
    }

    [Fact]
    public void OnTime_BetweenStations_IsInterpolatedLinearly()
    {
        var position = Locate(Trip, NoUpdates, T(8, 5)); // halfway through the A to B leg

        Assert.Equal(PositionKind.BetweenStops, position.Kind);
        Assert.Equal(50.05, position.Lat, 6);
        Assert.Equal("A", position.FromStopId);
        Assert.Equal("B", position.ToStopId);
        Assert.Equal(0, position.DelaySeconds);
        Assert.InRange(position.SpeedKmh, 66.0, 67.5); // about 11.1 km in 10 minutes
    }

    [Fact]
    public void Delay_AtFirstStop_IsCarriedToLaterStops()
    {
        var updates = new[] { new StopDelay("A", 300, 300) };

        // With 5 minutes delay: A leaves 08:05, B is reached 08:15, so 08:10 is halfway.
        var position = Locate(Trip, updates, T(8, 10));

        Assert.Equal(50.05, position.Lat, 6);
        Assert.Equal(300, position.DelaySeconds);
    }

    [Fact]
    public void Delay_ReportedForLaterStop_AppliesToStopsAfterIt()
    {
        var updates = new[] { new StopDelay("B", 120, 120) };

        // B leaves 08:14 and C is reached 08:32, so 08:23 is halfway between B and C.
        var position = Locate(Trip, updates, T(8, 23));

        Assert.Equal(50.15, position.Lat, 6);
    }

    [Fact]
    public void Delay_ExtendsTheTimeTheTripIsActive()
    {
        var updates = new[] { new StopDelay("A", 600, 600) };

        Assert.Null(PositionEstimator.Estimate(Trip, NoUpdates, T(8, 35)));
        Assert.NotNull(PositionEstimator.Estimate(Trip, updates, T(8, 35)));
    }

    [Fact]
    public void EarlyTrain_DoesNotLeaveBeforeThePlannedDeparture()
    {
        var updates = new[] { new StopDelay("B", -120, -120) };

        // Arrives at B 08:08 but may not leave before 08:12, so at 08:10 it is still waiting there.
        var position = Locate(Trip, updates, T(8, 10));

        Assert.Equal(PositionKind.AtStop, position.Kind);
        Assert.Equal(50.1, position.Lat, 6);
        Assert.Equal(0, position.DelaySeconds);
    }

    [Fact]
    public void SkippedStop_IsLeftOutOfTheRoute()
    {
        var updates = new[] { new StopDelay("B", null, null, Skipped: true) };

        var position = Locate(Trip, updates, T(8, 15)); // halfway from A (08:00) to C (08:30)

        Assert.Equal(PositionKind.BetweenStops, position.Kind);
        Assert.Equal("A", position.FromStopId);
        Assert.Equal("C", position.ToStopId);
        Assert.Equal(50.1, position.Lat, 6);
    }

    [Fact]
    public void UpdateForUnknownStop_IsIgnored()
    {
        var updates = new[] { new StopDelay("ZZZ", 600, 600) };

        var position = Locate(Trip, updates, T(8, 5));

        Assert.Equal(50.05, position.Lat, 6);
        Assert.Equal(0, position.DelaySeconds);
    }

    [Fact]
    public void TimesPastMidnight_AreHandled()
    {
        // GTFS writes 01:10 on the next day as 25:10:00.
        var late = new[]
        {
            new ScheduledStop("D", 50.0, 13.0, T(23, 55), T(23, 55)),
            new ScheduledStop("E", 50.1, 13.0, T(24, 10), T(24, 10)),
        };

        var position = Locate(late, NoUpdates, T(24, 2) + 30); // 00:02:30 next day, halfway

        Assert.Equal(50.05, position.Lat, 6);
    }
}

public class ServiceDayTests
{
    [Fact]
    public void NormalDay_StartsAtLocalMidnight()
    {
        // 1 October 2026: Berlin is on summer time (UTC+2), so local midnight is 22:00 UTC the day before.
        var start = ServiceDay.Start(new DateOnly(2026, 10, 1));

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void DayWhenClocksChange_StartsAtNoonMinus12Hours()
    {
        // 29 March 2026: clocks go forward at night. Noon is UTC+2, so the start is 22:00 UTC,
        // which is 23:00 local time the evening before.
        var start = ServiceDay.Start(new DateOnly(2026, 3, 29));

        Assert.Equal(new DateTimeOffset(2026, 3, 28, 22, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void SecondsSinceStart_CountsFromServiceDayStart()
    {
        var now = new DateTimeOffset(2026, 10, 1, 6, 30, 0, TimeSpan.Zero); // 08:30 in Berlin

        Assert.Equal(8 * 3600 + 30 * 60, ServiceDay.SecondsSinceStart(new DateOnly(2026, 10, 1), now));
    }
}