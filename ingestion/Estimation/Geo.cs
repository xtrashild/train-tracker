namespace Ingestion.Estimation;

public static class Geo
{
    /// <summary>Great-circle distance in metres (haversine formula).</summary>
    public static double DistanceMetres(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusMetres = 6_371_000;
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMetres * Math.Asin(Math.Sqrt(h));
    }
}