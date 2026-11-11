namespace LupiraLocationApi.Core.Application.Telemetry;

/// <summary>One accurate, non-mock fix as the rollup sees it.</summary>
internal readonly record struct RollupPoint(DateTimeOffset Ts, double Lat, double Lon, double? SpeedMps, double? SpeedAccMps)
{
    /// <summary>GNSS speed, or null when the device reported none or flagged it as unreliable (same bar as movement stats).</summary>
    public double? TrustedSpeedMps => SpeedMps is { } s && (SpeedAccMps is null || SpeedAccMps <= 2) ? s : null;
}
