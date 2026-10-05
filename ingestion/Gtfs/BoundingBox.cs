using System.Globalization;

namespace Ingestion.Gtfs;

/// <summary>A rectangle in latitude and longitude, used to pick the region to import.</summary>
public sealed record BoundingBox(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    /// <summary>Roughly Dresden, Chemnitz and the area between and around them.</summary>
    public static BoundingBox Default { get; } = new(50.7, 12.8, 51.4, 14.3);

    public bool Contains(double lat, double lon) =>
        lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;

    /// <summary>Parses "minLat,minLon,maxLat,maxLon".</summary>
    public static BoundingBox Parse(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !TryParse(parts[0], out var minLat) || !TryParse(parts[1], out var minLon)
            || !TryParse(parts[2], out var maxLat) || !TryParse(parts[3], out var maxLon)
            || minLat >= maxLat || minLon >= maxLon)
        {
            throw new FormatException("Expected --bbox minLat,minLon,maxLat,maxLon, for example 50.7,12.8,51.4,14.3");
        }
        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private static bool TryParse(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public override string ToString() => $"{MinLat},{MinLon},{MaxLat},{MaxLon}";
}