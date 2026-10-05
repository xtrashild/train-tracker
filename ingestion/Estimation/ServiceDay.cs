namespace Ingestion.Estimation;

/// <summary>GTFS times such as 08:05:00 are relative to the start of a "service day".
/// By definition that start is noon on the service date, minus 12 hours, in the agency's local time.
/// On most days this is local midnight. On days when the clocks change it is one hour off,
/// which is why the calculation goes through noon.</summary>
public static class ServiceDay
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DateTimeOffset Start(DateOnly serviceDate)
    {
        var noon = serviceDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var noonLocal = new DateTimeOffset(noon, Berlin.GetUtcOffset(noon));
        return noonLocal.AddHours(-12);
    }

    /// <summary>Seconds from the start of the service day until <paramref name="now"/>.</summary>
    public static int SecondsSinceStart(DateOnly serviceDate, DateTimeOffset now) =>
        (int)(now - Start(serviceDate)).TotalSeconds;
}