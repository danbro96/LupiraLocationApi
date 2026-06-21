namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>A single validated GPS fix from an ingest batch (in-memory; principal/device are stamped server-side, not
/// carried here).</summary>
public sealed record LocationFix(
    long Seq,
    DateTimeOffset Ts,
    double Lat,
    double Lon,
    double? AccuracyM,
    double? AltitudeM,
    double? VerticalAccM,
    double? HeadingDeg,
    double? HeadingAccDeg,
    double? SpeedMps,
    double? SpeedAccMps,
    LocationProvider Provider,
    MotionActivity Activity,
    short? ActivityConf,
    short? BatteryPct,
    bool? IsMoving,
    bool IsMock);
