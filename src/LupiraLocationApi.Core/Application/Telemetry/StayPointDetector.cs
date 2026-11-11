namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>A stay opens when consecutive fixes remain within <see cref="RoamRadiusM"/> of their running centroid for at
/// least <see cref="MinDwell"/>. Indoor jitter splits one stay into several, so neighbours close in space and time merge.</summary>
internal static class StayPointDetector
{
    public const double RoamRadiusM = 80.0;
    public const double MergeRadiusM = 120.0;
    public const double MergeExcursionM = 250.0;
    public static readonly TimeSpan MinDwell = TimeSpan.FromMinutes(8);
    public static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(30);

    public static List<StayPoint> Detect(IReadOnlyList<RollupPoint> p)
    {
        var merged = new List<StayPoint>();
        foreach (var stay in Cluster(p))
        {
            if (merged.Count > 0 && TryMerge(p, merged[^1], stay) is { } joined) merged[^1] = joined;
            else merged.Add(stay);
        }

        return merged;
    }

    private static List<StayPoint> Cluster(IReadOnlyList<RollupPoint> p)
    {
        var stays = new List<StayPoint>();
        var n = p.Count;
        var i = 0;
        while (i < n)
        {
            double sumLat = p[i].Lat, sumLon = p[i].Lon;
            var j = i + 1;
            while (j < n && Geo.HaversineMeters(sumLat / (j - i), sumLon / (j - i), p[j].Lat, p[j].Lon) <= RoamRadiusM)
            {
                sumLat += p[j].Lat;
                sumLon += p[j].Lon;
                j++;
            }

            if (j - 1 > i && p[j - 1].Ts - p[i].Ts >= MinDwell)
            {
                var cLat = sumLat / (j - i);
                var cLon = sumLon / (j - i);
                double radius = 0;
                for (var k = i; k < j; k++) radius = Math.Max(radius, Geo.HaversineMeters(cLat, cLon, p[k].Lat, p[k].Lon));
                stays.Add(new StayPoint(i, j - 1, p[i].Ts, p[j - 1].Ts, cLat, cLon, radius, j - i, MaxGapS(p, i, j - 1)));
                i = j;
            }
            else
            {
                i++;
            }
        }

        return stays;
    }

    private static StayPoint? TryMerge(IReadOnlyList<RollupPoint> p, StayPoint a, StayPoint b)
    {
        if (b.Arrive - a.Depart > MergeGap || Geo.HaversineMeters(a.Lat, a.Lon, b.Lat, b.Lon) > MergeRadiusM) return null;

        var count = a.Count + b.Count;
        var lat = ((a.Lat * a.Count) + (b.Lat * b.Count)) / count;
        var lon = ((a.Lon * a.Count) + (b.Lon * b.Count)) / count;
        for (var k = a.LastIndex + 1; k < b.FirstIndex; k++)
        {
            if (Geo.HaversineMeters(lat, lon, p[k].Lat, p[k].Lon) > MergeExcursionM) return null;
        }

        var radius = Math.Max(
            a.RadiusM + Geo.HaversineMeters(a.Lat, a.Lon, lat, lon),
            b.RadiusM + Geo.HaversineMeters(b.Lat, b.Lon, lat, lon));
        var maxGapS = Math.Max(Math.Max(a.MaxGapS, b.MaxGapS), MaxGapS(p, a.LastIndex, b.FirstIndex));
        return new StayPoint(a.FirstIndex, b.LastIndex, a.Arrive, b.Depart, lat, lon, radius, count, maxGapS);
    }

    private static double MaxGapS(IReadOnlyList<RollupPoint> p, int first, int last)
    {
        double max = 0;
        for (var k = first + 1; k <= last; k++) max = Math.Max(max, (p[k].Ts - p[k - 1].Ts).TotalSeconds);
        return max;
    }
}
