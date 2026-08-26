namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>Great-circle distance (Haversine), metres.</summary>
internal static class Geo
{
    private const double EarthRadiusM = 6_371_000.0;

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Deg2Rad(lat2 - lat1);
        var dLon = Deg2Rad(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
              + (Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return EarthRadiusM * 2 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
}
