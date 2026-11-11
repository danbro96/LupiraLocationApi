using LupiraLocationApi.Core.Domain.Telemetry;

namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>A trip's mode is the one covering the most distance, judged per step from displacement and GNSS speed.
/// Per-fix labels can't carry it: underground or inside a vehicle the phone reports little or no speed.</summary>
internal static class TripModeClassifier
{
    public const double StepNoiseM = 30;
    public const double VehicleMps = 8;
    public const double CycleMps = 4.5;
    public const double RunMps = 2.5;
    public const double CycleCeilingMps = 12;

    public static MotionActivity Classify(IReadOnlyList<RollupPoint> seg)
    {
        var distanceByMode = new Dictionary<MotionActivity, double>();
        for (var m = 1; m < seg.Count; m++)
        {
            var d = Geo.HaversineMeters(seg[m - 1].Lat, seg[m - 1].Lon, seg[m].Lat, seg[m].Lon);
            var dt = (seg[m].Ts - seg[m - 1].Ts).TotalSeconds;
            if (d < StepNoiseM || dt <= 0) continue;
            var mode = StepMode(d / dt, Max(seg[m - 1].TrustedSpeedMps, seg[m].TrustedSpeedMps));
            distanceByMode[mode] = distanceByMode.GetValueOrDefault(mode) + d;
        }

        if (distanceByMode.Count == 0) return MotionActivity.Unknown;
        var dominant = distanceByMode.MaxBy(kv => kv.Value).Key;
        var topSpeed = seg.Max(s => s.TrustedSpeedMps);
        return dominant == MotionActivity.Cycle && topSpeed >= CycleCeilingMps ? MotionActivity.Vehicle : dominant;
    }

    private static MotionActivity StepMode(double impliedMps, double? gnssMps)
    {
        if (impliedMps >= VehicleMps || gnssMps >= VehicleMps) return MotionActivity.Vehicle;
        if (gnssMps >= CycleMps) return MotionActivity.Cycle;
        if (gnssMps >= RunMps) return MotionActivity.Run;

        // Fast displacement the GNSS never registered: underground or indoor transit.
        return impliedMps >= RunMps ? MotionActivity.Vehicle : MotionActivity.Walk;
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
