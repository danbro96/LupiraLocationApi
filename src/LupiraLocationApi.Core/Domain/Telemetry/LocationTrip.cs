namespace LupiraLocationApi.Domain.Telemetry;

/// <summary>Movement between two stays. Materialized by the rollup job into the <c>health</c> schema.</summary>
public sealed class LocationTrip
{
    public Guid Id { get; set; }
    public Guid PrincipalId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTimeOffset StartTs { get; set; }
    public DateTimeOffset EndTs { get; set; }
    public Guid? FromVisitId { get; set; }
    public Guid? ToVisitId { get; set; }
    public double DistanceM { get; set; }
    public double DurationS { get; set; }
    public MotionActivity DominantActivity { get; set; }
    public double AvgSpeedMps { get; set; }
    public double MaxSpeedMps { get; set; }
}
