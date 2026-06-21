using LupiraLocationApi.Domain.Telemetry;
using Npgsql;

namespace LupiraLocationApi.Application.Telemetry;

/// <summary>Great-circle distance (Haversine), metres.</summary>
internal static class Geo
{
    private const double EarthRadiusM = 6_371_000.0;

    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Deg2Rad(lat2 - lat1);
        var dLon = Deg2Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusM * 2 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
}

/// <summary>Small null-aware readers over the raw telemetry result sets (real columns come back as <c>float</c>,
/// smallints as <c>short</c> — normalize them to the DTO shapes).</summary>
internal static class Db
{
    public static double? NDouble(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    public static double Double0(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? 0.0 : Convert.ToDouble(r.GetValue(i));
    public static int? NInt(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    public static short? NShort(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt16(i);

    public static MotionActivity? Activity(short? a) =>
        a is null ? null : Enum.IsDefined((MotionActivity)a.Value) ? (MotionActivity)a.Value : MotionActivity.Unknown;
    public static LocationProvider? Provider(short? p) =>
        p is null ? null : Enum.IsDefined((LocationProvider)p.Value) ? (LocationProvider)p.Value : LocationProvider.Unknown;
}
