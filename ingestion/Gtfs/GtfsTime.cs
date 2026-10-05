using System.Globalization;

namespace Ingestion.Gtfs;

public static class GtfsTime
{
    /// <summary>Converts a GTFS time such as "08:05:00" or "25:10:00" to seconds since the start
    /// of the service day. Hours can be 24 or more. Returns null for empty or invalid values.</summary>
    public static int? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            || m > 59 || s > 59)
        {
            return null;
        }

        return h * 3600 + m * 60 + s;
    }
}